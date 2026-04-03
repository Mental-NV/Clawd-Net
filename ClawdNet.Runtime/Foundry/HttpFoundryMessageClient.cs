using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Exceptions;
using ClawdNet.Core.Models;

namespace ClawdNet.Runtime.Foundry;

/// <summary>
/// Azure Foundry API client implementing IModelClient.
/// Uses the same Anthropic messages API format on Azure Foundry endpoints.
/// Supports SSE streaming with buffered fallback and API key auth.
/// </summary>
public sealed class HttpFoundryMessageClient : IModelClient
{
    private readonly HttpClient _httpClient;
    private readonly FoundryCredentialResolver _credentials;
    private readonly string _modelName;

    public HttpFoundryMessageClient(HttpClient httpClient, string modelName, FoundryCredentialResolver? credentials = null)
    {
        _httpClient = httpClient;
        _modelName = modelName;
        _credentials = credentials ?? new FoundryCredentialResolver();

        if (!_credentials.HasCredentials)
        {
            throw new ModelProviderConfigurationException("foundry",
                "Foundry API key is not set. Set ANTHROPIC_FOUNDRY_API_KEY, " +
                "or set CLAUDE_CODE_SKIP_FOUNDRY_AUTH=1 for development.");
        }

        var baseUrl = _credentials.BuildBaseUrl();
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new ModelProviderConfigurationException("foundry",
                "Foundry base URL is not set. Set ANTHROPIC_FOUNDRY_BASE_URL or ANTHROPIC_FOUNDRY_RESOURCE.");
        }
    }

    public async Task<ModelResponse> SendAsync(ModelRequest request, CancellationToken cancellationToken)
    {
        var baseUrl = _credentials.BuildBaseUrl();
        var endpoint = $"{baseUrl}/v1/messages";

        var payload = BuildPayload(request, stream: false);
        var requestBody = payload.ToJsonString();

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint);
        httpRequest.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");

        ApplyAuthHeaders(httpRequest);

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new ModelProviderConfigurationException("foundry",
                $"API request failed with {(int)response.StatusCode}: {responseText}");
        }

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        return ParseBufferedResponse(responseJson);
    }

    public async IAsyncEnumerable<ModelStreamEvent> StreamAsync(
        ModelRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var baseUrl = _credentials.BuildBaseUrl();
        var endpoint = $"{baseUrl}/v1/messages";

        var payload = BuildPayload(request, stream: true);
        var requestBody = payload.ToJsonString();

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint);
        httpRequest.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");
        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        ApplyAuthHeaders(httpRequest);

        using var response = await _httpClient.SendAsync(
            httpRequest,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new ModelProviderConfigurationException("foundry",
                $"API request failed with {(int)response.StatusCode}: {responseText}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        string? currentEvent = null;
        var dataBuilder = new StringBuilder();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                if (!string.IsNullOrWhiteSpace(currentEvent) && dataBuilder.Length > 0)
                {
                    foreach (var parsedEvent in ParseServerSentEvent(currentEvent, dataBuilder.ToString()))
                    {
                        yield return parsedEvent;
                    }
                }

                currentEvent = null;
                dataBuilder.Clear();
                continue;
            }

            if (line.StartsWith("event:", StringComparison.Ordinal))
            {
                currentEvent = line["event:".Length..].Trim();
                continue;
            }

            if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                if (dataBuilder.Length > 0)
                {
                    dataBuilder.Append('\n');
                }

                dataBuilder.Append(line["data:".Length..].TrimStart());
            }
        }

        // Flush remaining
        if (!string.IsNullOrWhiteSpace(currentEvent) && dataBuilder.Length > 0)
        {
            foreach (var parsedEvent in ParseServerSentEvent(currentEvent, dataBuilder.ToString()))
            {
                yield return parsedEvent;
            }
        }
    }

    private void ApplyAuthHeaders(HttpRequestMessage request)
    {
        if (_credentials.UseSkipAuth)
        {
            return;
        }

        var apiKey = _credentials.GetApiKey();
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            request.Headers.Add("api-key", apiKey);
        }
    }

    private static JsonObject BuildPayload(ModelRequest request, bool stream)
    {
        var messages = new JsonArray();
        foreach (var message in request.Messages)
        {
            JsonNode contentNode = message.Role switch
            {
                "tool_result" => new JsonArray
                {
                    new JsonObject
                    {
                        ["type"] = "tool_result",
                        ["tool_use_id"] = message.ToolCallId,
                        ["content"] = message.Content,
                        ["is_error"] = message.IsError
                    }
                },
                _ => message.Content
            };

            messages.Add(new JsonObject
            {
                ["role"] = message.Role is "assistant" ? "assistant" : "user",
                ["content"] = contentNode
            });
        }

        var tools = new JsonArray();
        foreach (var tool in request.Tools)
        {
            tools.Add(new JsonObject
            {
                ["name"] = tool.Name,
                ["description"] = tool.Description,
                ["input_schema"] = tool.InputSchema
            });
        }

        return new JsonObject
        {
            ["model"] = request.Model,
            ["max_tokens"] = 1024,
            ["system"] = request.SystemPrompt,
            ["messages"] = messages,
            ["tools"] = tools,
            ["stream"] = stream
        };
    }

    private static ModelResponse ParseBufferedResponse(string responseJson)
    {
        using var document = JsonDocument.Parse(responseJson);
        var root = document.RootElement;
        var model = root.GetProperty("model").GetString() ?? string.Empty;
        var stopReason = root.TryGetProperty("stop_reason", out var stopElement)
            ? stopElement.GetString() ?? string.Empty
            : string.Empty;

        var blocks = new List<ModelContentBlock>();
        foreach (var contentBlock in root.GetProperty("content").EnumerateArray())
        {
            var type = contentBlock.GetProperty("type").GetString();
            switch (type)
            {
                case "text":
                    blocks.Add(new TextContentBlock(contentBlock.GetProperty("text").GetString() ?? string.Empty));
                    break;
                case "tool_use":
                    blocks.Add(new ToolUseContentBlock(
                        contentBlock.GetProperty("id").GetString() ?? Guid.NewGuid().ToString("N"),
                        contentBlock.GetProperty("name").GetString() ?? string.Empty,
                        contentBlock.TryGetProperty("input", out var inputElement) ? JsonNode.Parse(inputElement.GetRawText()) : null));
                    break;
            }
        }

        return new ModelResponse(model, blocks, stopReason);
    }

    private static IEnumerable<ModelStreamEvent> ParseServerSentEvent(string eventType, string data)
    {
        if (string.Equals(data, "[DONE]", StringComparison.Ordinal))
        {
            yield break;
        }

        using var document = JsonDocument.Parse(data);
        var root = document.RootElement;
        switch (eventType)
        {
            case "message_start":
                yield return new MessageStartedEvent(
                    root.TryGetProperty("message", out var msgEl) && msgEl.TryGetProperty("model", out var modelEl)
                        ? modelEl.GetString() ?? string.Empty
                        : string.Empty);
                break;
            case "content_block_delta":
                var delta = root.GetProperty("delta");
                var deltaType = delta.TryGetProperty("type", out var dtEl) ? dtEl.GetString() : null;
                if (string.Equals(deltaType, "text_delta", StringComparison.Ordinal))
                {
                    yield return new TextDeltaEvent(delta.GetProperty("text").GetString() ?? string.Empty);
                }
                else if (string.Equals(deltaType, "input_json_delta", StringComparison.Ordinal))
                {
                    var name = root.TryGetProperty("content_block", out var blockEl) &&
                               blockEl.TryGetProperty("name", out var blockName)
                        ? blockName.GetString() ?? string.Empty
                        : string.Empty;
                    yield return new ToolUseInputDeltaEvent(
                        root.TryGetProperty("content_block", out var cbEl) && cbEl.TryGetProperty("id", out var idEl)
                            ? idEl.GetString() ?? Guid.NewGuid().ToString("N")
                            : Guid.NewGuid().ToString("N"),
                        name,
                        delta.TryGetProperty("partial_json", out var pjEl) ? pjEl.GetString() ?? string.Empty : string.Empty);
                }
                break;
            case "content_block_start":
                var contentBlock = root.TryGetProperty("content_block", out var cb) ? cb : default;
                if (contentBlock.ValueKind != JsonValueKind.Undefined &&
                    string.Equals(contentBlock.GetProperty("type").GetString(), "tool_use", StringComparison.Ordinal))
                {
                    yield return new ToolUseStartedEvent(
                        contentBlock.GetProperty("id").GetString() ?? Guid.NewGuid().ToString("N"),
                        contentBlock.GetProperty("name").GetString() ?? string.Empty);
                }
                break;
            case "content_block_stop":
                if (root.TryGetProperty("content_block", out var stoppedBlock))
                {
                    var stoppedType = stoppedBlock.GetProperty("type").GetString();
                    if (string.Equals(stoppedType, "text", StringComparison.Ordinal))
                    {
                        yield return new TextCompletedEvent(stoppedBlock.GetProperty("text").GetString() ?? string.Empty);
                    }
                    else if (string.Equals(stoppedType, "tool_use", StringComparison.Ordinal))
                    {
                        yield return new ToolUseCompletedEvent(
                            stoppedBlock.GetProperty("id").GetString() ?? Guid.NewGuid().ToString("N"),
                            stoppedBlock.GetProperty("name").GetString() ?? string.Empty,
                            stoppedBlock.TryGetProperty("input", out var inputElement) ? JsonNode.Parse(inputElement.GetRawText()) : null);
                    }
                }
                break;
            case "message_delta":
                if (root.TryGetProperty("delta", out var messageDelta) &&
                    messageDelta.TryGetProperty("stop_reason", out var stopReason))
                {
                    yield return new MessageCompletedEvent(stopReason.GetString() ?? string.Empty);
                }
                break;
            case "message_stop":
                yield break;
            case "error":
                yield return new ModelErrorEvent(
                    root.TryGetProperty("error", out var errorElement)
                        ? errorElement.TryGetProperty("message", out var msgEl2)
                            ? msgEl2.GetString() ?? "Foundry streaming error."
                            : "Foundry streaming error."
                        : "Foundry streaming error.");
                break;
        }
    }
}
