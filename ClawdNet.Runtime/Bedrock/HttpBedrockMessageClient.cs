using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Exceptions;
using ClawdNet.Core.Models;

namespace ClawdNet.Runtime.Bedrock;

/// <summary>
/// AWS Bedrock Converse API client implementing IModelClient.
/// Supports SSE streaming with buffered fallback, AWS SigV4 signing, and bearer token auth.
/// </summary>
public sealed class HttpBedrockMessageClient : IModelClient
{
    private readonly HttpClient _httpClient;
    private readonly BedrockCredentialResolver _credentials;
    private readonly string _modelId;

    public HttpBedrockMessageClient(HttpClient httpClient, string modelId, BedrockCredentialResolver? credentials = null)
    {
        _httpClient = httpClient;
        _modelId = modelId;
        _credentials = credentials ?? new BedrockCredentialResolver();

        if (!_credentials.HasCredentials)
        {
            throw new ModelProviderConfigurationException("bedrock",
                "AWS credentials are not set. Set AWS_ACCESS_KEY_ID and AWS_SECRET_ACCESS_KEY, " +
                "or AWS_BEARER_TOKEN_BEDROCK, or set CLAUDE_CODE_SKIP_BEDROCK_AUTH=1 for development.");
        }
    }

    public async Task<ModelResponse> SendAsync(ModelRequest request, CancellationToken cancellationToken)
    {
        var payload = BuildPayload(request, stream: false);
        var requestBody = payload.ToJsonString();
        var payloadHash = BedrockCredentialResolver.ComputePayloadHash(requestBody);
        var endpoint = _credentials.BuildEndpoint(_modelId);
        var uri = new Uri(endpoint).PathAndQuery;
        var utcNow = DateTime.UtcNow;

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint);
        httpRequest.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");

        ApplyAuthHeaders(httpRequest, "POST", uri, payloadHash, requestBody, utcNow);

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new ModelProviderConfigurationException("bedrock",
                $"API request failed with {(int)response.StatusCode}: {responseText}");
        }

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        return ParseBufferedResponse(responseJson);
    }

    public async IAsyncEnumerable<ModelStreamEvent> StreamAsync(
        ModelRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var payload = BuildPayload(request, stream: true);
        var requestBody = payload.ToJsonString();
        var payloadHash = BedrockCredentialResolver.ComputePayloadHash(requestBody);
        var endpoint = _credentials.BuildEndpoint(_modelId);
        var uri = new Uri(endpoint).PathAndQuery;
        var utcNow = DateTime.UtcNow;

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint);
        httpRequest.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");
        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        ApplyAuthHeaders(httpRequest, "POST", uri, payloadHash, requestBody, utcNow);

        using var response = await _httpClient.SendAsync(
            httpRequest,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new ModelProviderConfigurationException("bedrock",
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

    private void ApplyAuthHeaders(
        HttpRequestMessage request,
        string method,
        string uri,
        string payloadHash,
        string requestBody,
        DateTime utcNow)
    {
        if (_credentials.UseSkipAuth)
        {
            // No auth headers needed
            return;
        }

        if (_credentials.UseBearerAuth)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _credentials.GetBearerToken());
            return;
        }

        // Standard AWS SigV4 signing
        var amzDate = BedrockCredentialResolver.GetAmzDate(utcNow);
        var authorization = _credentials.ComputeSignature(method, uri, payloadHash, requestBody, utcNow);

        request.Headers.TryAddWithoutValidation("x-amz-date", amzDate);
        request.Headers.TryAddWithoutValidation("Authorization", authorization);

        if (!string.IsNullOrWhiteSpace(_credentials.SessionToken))
        {
            request.Headers.TryAddWithoutValidation("x-amz-security-token", _credentials.SessionToken);
        }
    }

    private static JsonObject BuildPayload(ModelRequest request, bool stream)
    {
        var messages = new JsonArray();
        if (!string.IsNullOrWhiteSpace(request.SystemPrompt))
        {
            messages.Add(new JsonObject
            {
                ["role"] = "user",
                ["content"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["type"] = "text",
                        ["text"] = request.SystemPrompt
                    }
                }
            });
        }

        foreach (var message in request.Messages)
        {
            switch (message.Role)
            {
                case "tool_result":
                    messages.Add(new JsonObject
                    {
                        ["role"] = "user",
                        ["content"] = new JsonArray
                        {
                            new JsonObject
                            {
                                ["type"] = "tool_result",
                                ["tool_use_id"] = message.ToolCallId,
                                ["content"] = message.Content,
                                ["is_error"] = message.IsError
                            }
                        }
                    });
                    break;
                case "assistant":
                    // Build assistant content blocks
                    var assistantBlocks = new JsonArray();
                    if (!string.IsNullOrWhiteSpace(message.Content))
                    {
                        assistantBlocks.Add(new JsonObject
                        {
                            ["type"] = "text",
                            ["text"] = message.Content
                        });
                    }
                    messages.Add(new JsonObject
                    {
                        ["role"] = "assistant",
                        ["content"] = assistantBlocks
                    });
                    break;
                default:
                    messages.Add(new JsonObject
                    {
                        ["role"] = "user",
                        ["content"] = new JsonArray
                        {
                            new JsonObject
                            {
                                ["type"] = "text",
                                ["text"] = message.Content
                            }
                        }
                    });
                    break;
            }
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

        var payload = new JsonObject
        {
            ["modelId"] = request.Model,
            ["messages"] = messages,
            ["tools"] = tools,
            ["inferenceConfig"] = new JsonObject
            {
                ["maxTokens"] = 4096
            }
        };

        if (stream)
        {
            payload["additionalModelRequestFields"] = new JsonObject
            {
                ["stream"] = true
            };
        }

        return payload;
    }

    private static ModelResponse ParseBufferedResponse(string responseJson)
    {
        using var document = JsonDocument.Parse(responseJson);
        var root = document.RootElement;
        var model = root.GetProperty("output").GetProperty("message").GetProperty("modelId").GetString() ?? string.Empty;
        var blocks = new List<ModelContentBlock>();
        var stopReason = string.Empty;

        if (root.TryGetProperty("stopReason", out var stopElement))
        {
            stopReason = stopElement.GetString() ?? string.Empty;
        }

        if (root.TryGetProperty("output", out var outputElement) &&
            outputElement.TryGetProperty("message", out var messageElement) &&
            messageElement.TryGetProperty("content", out var contentElement))
        {
            foreach (var contentBlock in contentElement.EnumerateArray())
            {
                var type = contentBlock.GetProperty("type").GetString();
                switch (type)
                {
                    case "text":
                        blocks.Add(new TextContentBlock(
                            contentBlock.TryGetProperty("text", out var textElement)
                                ? textElement.GetString() ?? string.Empty
                                : string.Empty));
                        break;
                    case "tool_use":
                        blocks.Add(new ToolUseContentBlock(
                            contentBlock.TryGetProperty("id", out var idElement)
                                ? idElement.GetString() ?? Guid.NewGuid().ToString("N")
                                : Guid.NewGuid().ToString("N"),
                            contentBlock.TryGetProperty("name", out var nameElement)
                                ? nameElement.GetString() ?? string.Empty
                                : string.Empty,
                            contentBlock.TryGetProperty("input", out var inputElement)
                                ? JsonNode.Parse(inputElement.GetRawText())
                                : null));
                        break;
                }
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
            case "messageStart":
                yield return new MessageStartedEvent(
                    root.TryGetProperty("message", out var msgElement) &&
                    msgElement.TryGetProperty("modelId", out var modelElement)
                        ? modelElement.GetString() ?? string.Empty
                        : string.Empty);
                break;

            case "contentBlockStart":
                if (root.TryGetProperty("start", out var startBlock) &&
                    startBlock.TryGetProperty("type", out var startType) &&
                    string.Equals(startType.GetString(), "tool_use", StringComparison.Ordinal))
                {
                    yield return new ToolUseStartedEvent(
                        startBlock.TryGetProperty("id", out var startId)
                            ? startId.GetString() ?? Guid.NewGuid().ToString("N")
                            : Guid.NewGuid().ToString("N"),
                        startBlock.TryGetProperty("name", out var startName)
                            ? startName.GetString() ?? string.Empty
                            : string.Empty);
                }
                break;

            case "contentBlockDelta":
                if (!root.TryGetProperty("delta", out var delta))
                {
                    yield break;
                }

                var deltaType = delta.TryGetProperty("type", out var dtElement) ? dtElement.GetString() : null;
                if (string.Equals(deltaType, "text", StringComparison.Ordinal))
                {
                    yield return new TextDeltaEvent(
                        delta.TryGetProperty("text", out var textElement)
                            ? textElement.GetString() ?? string.Empty
                            : string.Empty);
                }
                else if (string.Equals(deltaType, "input_json", StringComparison.Ordinal))
                {
                    var toolUseId = root.TryGetProperty("contentBlockIndex", out var cbIdx)
                        ? cbIdx.GetInt32().ToString()
                        : Guid.NewGuid().ToString("N");
                    var toolName = string.Empty;
                    yield return new ToolUseInputDeltaEvent(
                        toolUseId,
                        toolName,
                        delta.TryGetProperty("text", out var partialElement)
                            ? partialElement.GetString() ?? string.Empty
                            : string.Empty);
                }
                break;

            case "contentBlockStop":
                // Text completed
                if (root.TryGetProperty("contentBlockIndex", out var stopIdx))
                {
                    yield return new TextCompletedEvent(string.Empty);
                }
                break;

            case "messageStop":
                yield return new MessageCompletedEvent(
                    root.TryGetProperty("stopReason", out var stopReasonElement)
                        ? stopReasonElement.GetString() ?? "stop"
                        : "stop");
                break;

            case "metadata":
                // Optional metadata event, ignore for now
                break;

            case "exception":
                yield return new ModelErrorEvent(
                    root.TryGetProperty("message", out var excMsg)
                        ? excMsg.GetString() ?? "Bedrock streaming error."
                        : "Bedrock streaming error.");
                break;
        }
    }
}
