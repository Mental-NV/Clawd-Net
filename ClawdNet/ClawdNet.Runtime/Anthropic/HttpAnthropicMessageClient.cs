using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Runtime.CompilerServices;
using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Exceptions;
using ClawdNet.Core.Models;

namespace ClawdNet.Runtime.Anthropic;

public sealed class HttpAnthropicMessageClient : IAnthropicMessageClient
{
    private readonly HttpClient _httpClient;
    private readonly Func<string?> _apiKeyAccessor;
    private readonly string _baseUrl;

    public HttpAnthropicMessageClient(HttpClient httpClient, Func<string?>? apiKeyAccessor = null, string? baseUrl = null)
    {
        _httpClient = httpClient;
        _apiKeyAccessor = apiKeyAccessor ?? (() => Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY"));
        _baseUrl = (baseUrl ?? Environment.GetEnvironmentVariable("ANTHROPIC_BASE_URL") ?? "https://api.anthropic.com").TrimEnd('/');
    }

    public async Task<ModelResponse> SendAsync(ModelRequest request, CancellationToken cancellationToken)
    {
        var apiKey = _apiKeyAccessor();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ModelProviderConfigurationException("anthropic", "API key is not set.");
        }

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/v1/messages");
        httpRequest.Headers.Add("x-api-key", apiKey);
        httpRequest.Headers.Add("anthropic-version", "2023-06-01");

        var payload = BuildPayload(request, stream: false);
        httpRequest.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new ModelProviderConfigurationException("anthropic", $"API request failed with {(int)response.StatusCode}: {responseText}");
        }

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        return ParseBufferedResponse(responseJson);
    }

    public async IAsyncEnumerable<ModelStreamEvent> StreamAsync(
        ModelRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var apiKey = _apiKeyAccessor();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ModelProviderConfigurationException("anthropic", "API key is not set.");
        }

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/v1/messages");
        httpRequest.Headers.Add("x-api-key", apiKey);
        httpRequest.Headers.Add("anthropic-version", "2023-06-01");
        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        var payload = BuildPayload(request, stream: true);
        httpRequest.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(
            httpRequest,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new ModelProviderConfigurationException("anthropic", $"API request failed with {(int)response.StatusCode}: {responseText}");
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

        if (!string.IsNullOrWhiteSpace(currentEvent) && dataBuilder.Length > 0)
        {
            foreach (var parsedEvent in ParseServerSentEvent(currentEvent, dataBuilder.ToString()))
            {
                yield return parsedEvent;
            }
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

    private static ModelResponse AggregateResponse(IEnumerable<ModelStreamEvent> streamEvents)
    {
        var model = string.Empty;
        var stopReason = string.Empty;
        var blocks = new List<ModelContentBlock>();
        foreach (var streamEvent in streamEvents)
        {
            switch (streamEvent)
            {
                case MessageStartedEvent started:
                    model = started.Model;
                    break;
                case TextCompletedEvent textCompleted:
                    blocks.Add(new TextContentBlock(textCompleted.Text));
                    break;
                case ToolUseCompletedEvent toolCompleted:
                    blocks.Add(new ToolUseContentBlock(toolCompleted.Id, toolCompleted.Name, toolCompleted.Input));
                    break;
                case MessageCompletedEvent completed:
                    stopReason = completed.StopReason;
                    break;
            }
        }

        return new ModelResponse(model, blocks, stopReason);
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
                yield return new MessageStartedEvent(root.GetProperty("message").GetProperty("model").GetString() ?? string.Empty);
                break;
            case "content_block_delta":
                var delta = root.GetProperty("delta");
                var deltaType = delta.GetProperty("type").GetString();
                if (string.Equals(deltaType, "text_delta", StringComparison.Ordinal))
                {
                    yield return new TextDeltaEvent(delta.GetProperty("text").GetString() ?? string.Empty);
                }
                else if (string.Equals(deltaType, "input_json_delta", StringComparison.Ordinal))
                {
                    var name = root.TryGetProperty("content_block", out var blockElement) &&
                               blockElement.TryGetProperty("name", out var blockName)
                        ? blockName.GetString() ?? string.Empty
                        : string.Empty;
                    yield return new ToolUseInputDeltaEvent(
                        root.TryGetProperty("content_block", out var contentBlockWithId) && contentBlockWithId.TryGetProperty("id", out var blockId)
                            ? blockId.GetString() ?? Guid.NewGuid().ToString("N")
                            : Guid.NewGuid().ToString("N"),
                        name,
                        delta.GetProperty("partial_json").GetString() ?? string.Empty);
                }
                break;
            case "content_block_start":
                var contentBlock = root.GetProperty("content_block");
                if (string.Equals(contentBlock.GetProperty("type").GetString(), "tool_use", StringComparison.Ordinal))
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
                yield return new ModelErrorEvent(root.TryGetProperty("error", out var errorElement)
                    ? errorElement.GetProperty("message").GetString() ?? "Anthropic streaming error."
                    : "Anthropic streaming error.");
                break;
        }
    }
}
