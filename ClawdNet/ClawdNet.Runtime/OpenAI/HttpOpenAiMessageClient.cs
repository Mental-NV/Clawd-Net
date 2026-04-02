using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Exceptions;
using ClawdNet.Core.Models;

namespace ClawdNet.Runtime.OpenAI;

public sealed class HttpOpenAiMessageClient : IModelClient
{
    private readonly HttpClient _httpClient;
    private readonly Func<string?> _apiKeyAccessor;
    private readonly string _baseUrl;

    public HttpOpenAiMessageClient(HttpClient httpClient, Func<string?>? apiKeyAccessor = null, string? baseUrl = null)
    {
        _httpClient = httpClient;
        _apiKeyAccessor = apiKeyAccessor ?? (() => Environment.GetEnvironmentVariable("OPENAI_API_KEY"));
        _baseUrl = (baseUrl ?? Environment.GetEnvironmentVariable("OPENAI_BASE_URL") ?? "https://api.openai.com").TrimEnd('/');
    }

    public async Task<ModelResponse> SendAsync(ModelRequest request, CancellationToken cancellationToken)
    {
        var apiKey = _apiKeyAccessor();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ModelProviderConfigurationException("openai", "API key is not set.");
        }

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/v1/chat/completions");
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        httpRequest.Content = new StringContent(BuildPayload(request, stream: false).ToJsonString(), Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new ModelProviderConfigurationException("openai", $"API request failed with {(int)response.StatusCode}: {responseText}");
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
            throw new ModelProviderConfigurationException("openai", "API key is not set.");
        }

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/v1/chat/completions");
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        httpRequest.Content = new StringContent(BuildPayload(request, stream: true).ToJsonString(), Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(
            httpRequest,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new ModelProviderConfigurationException("openai", $"API request failed with {(int)response.StatusCode}: {responseText}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        yield return new MessageStartedEvent(request.Model);

        var accumulatedText = new StringBuilder();
        var toolStates = new Dictionary<int, StreamingToolState>();
        var stopReason = string.Empty;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                break;
            }

            if (!line.StartsWith("data:", StringComparison.Ordinal))
            {
                continue;
            }

            var payload = line["data:".Length..].Trim();
            if (string.Equals(payload, "[DONE]", StringComparison.Ordinal))
            {
                break;
            }

            var parsed = ParseStreamPayload(payload, accumulatedText, toolStates);
            if (!string.IsNullOrWhiteSpace(parsed.StopReason))
            {
                stopReason = parsed.StopReason;
            }

            foreach (var parsedEvent in parsed.Events)
            {
                yield return parsedEvent;
            }
        }

        if (accumulatedText.Length > 0)
        {
            yield return new TextCompletedEvent(accumulatedText.ToString());
        }

        foreach (var toolState in toolStates.OrderBy(entry => entry.Key).Select(entry => entry.Value))
        {
            yield return new ToolUseCompletedEvent(toolState.Id, toolState.Name, ParseJson(toolState.Arguments.ToString()));
        }

        yield return new MessageCompletedEvent(string.IsNullOrWhiteSpace(stopReason) ? "stop" : stopReason);
    }

    private static JsonObject BuildPayload(ModelRequest request, bool stream)
    {
        var messages = new JsonArray();
        if (!string.IsNullOrWhiteSpace(request.SystemPrompt))
        {
            messages.Add(new JsonObject
            {
                ["role"] = "system",
                ["content"] = request.SystemPrompt
            });
        }

        foreach (var message in request.Messages)
        {
            switch (message.Role)
            {
                case "tool_use":
                    messages.Add(new JsonObject
                    {
                        ["role"] = "assistant",
                        ["content"] = JsonValue.Create((string?)null),
                        ["tool_calls"] = new JsonArray
                        {
                            new JsonObject
                            {
                                ["id"] = message.ToolCallId ?? Guid.NewGuid().ToString("N"),
                                ["type"] = "function",
                                ["function"] = new JsonObject
                                {
                                    ["name"] = message.ToolName ?? string.Empty,
                                    ["arguments"] = NormalizeToolArguments(message.Content)
                                }
                            }
                        }
                    });
                    break;
                case "tool_result":
                    messages.Add(new JsonObject
                    {
                        ["role"] = "tool",
                        ["tool_call_id"] = message.ToolCallId,
                        ["content"] = message.Content
                    });
                    break;
                default:
                    messages.Add(new JsonObject
                    {
                        ["role"] = NormalizeRole(message.Role),
                        ["content"] = message.Content
                    });
                    break;
            }
        }

        var tools = new JsonArray();
        foreach (var tool in request.Tools)
        {
            tools.Add(new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = tool.Name,
                    ["description"] = tool.Description,
                    ["parameters"] = tool.InputSchema
                }
            });
        }

        return new JsonObject
        {
            ["model"] = request.Model,
            ["messages"] = messages,
            ["tools"] = tools,
            ["stream"] = stream
        };
    }

    private static ModelResponse ParseBufferedResponse(string responseJson)
    {
        using var document = JsonDocument.Parse(responseJson);
        var root = document.RootElement;
        var model = root.TryGetProperty("model", out var modelElement)
            ? modelElement.GetString() ?? string.Empty
            : string.Empty;
        var blocks = new List<ModelContentBlock>();
        var finishReason = string.Empty;

        if (!root.TryGetProperty("choices", out var choicesElement))
        {
            return new ModelResponse(model, blocks, finishReason);
        }

        foreach (var choice in choicesElement.EnumerateArray())
        {
            finishReason = choice.TryGetProperty("finish_reason", out var finishElement)
                ? finishElement.GetString() ?? finishReason
                : finishReason;
            if (!choice.TryGetProperty("message", out var messageElement))
            {
                continue;
            }

            if (messageElement.TryGetProperty("content", out var contentElement) &&
                contentElement.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(contentElement.GetString()))
            {
                blocks.Add(new TextContentBlock(contentElement.GetString()!));
            }

            if (!messageElement.TryGetProperty("tool_calls", out var toolCallsElement) ||
                toolCallsElement.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var toolCall in toolCallsElement.EnumerateArray())
            {
                var function = toolCall.GetProperty("function");
                blocks.Add(new ToolUseContentBlock(
                    toolCall.GetProperty("id").GetString() ?? Guid.NewGuid().ToString("N"),
                    function.GetProperty("name").GetString() ?? string.Empty,
                    ParseJson(function.GetProperty("arguments").GetString())));
            }
        }

        return new ModelResponse(model, blocks, finishReason);
    }

    private static ParsedStreamPayload ParseStreamPayload(
        string payload,
        StringBuilder accumulatedText,
        Dictionary<int, StreamingToolState> toolStates)
    {
        var events = new List<ModelStreamEvent>();
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        if (root.TryGetProperty("error", out var errorElement))
        {
            var message = errorElement.TryGetProperty("message", out var messageElement)
                ? messageElement.GetString() ?? "OpenAI streaming error."
                : "OpenAI streaming error.";
            events.Add(new ModelErrorEvent(message));
            return new ParsedStreamPayload(events, null);
        }

        if (!root.TryGetProperty("choices", out var choicesElement))
        {
            return new ParsedStreamPayload(events, null);
        }

        string? stopReason = null;
        foreach (var choice in choicesElement.EnumerateArray())
        {
            if (choice.TryGetProperty("delta", out var delta))
            {
                if (delta.TryGetProperty("content", out var contentElement) &&
                    contentElement.ValueKind == JsonValueKind.String)
                {
                    var text = contentElement.GetString() ?? string.Empty;
                    if (!string.IsNullOrEmpty(text))
                    {
                        accumulatedText.Append(text);
                        events.Add(new TextDeltaEvent(text));
                    }
                }

                if (delta.TryGetProperty("tool_calls", out var toolCallsElement) &&
                    toolCallsElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var toolCall in toolCallsElement.EnumerateArray())
                    {
                        var index = toolCall.TryGetProperty("index", out var indexElement)
                            ? indexElement.GetInt32()
                            : toolStates.Count;
                        if (!toolStates.TryGetValue(index, out var state))
                        {
                            state = new StreamingToolState(Guid.NewGuid().ToString("N"), string.Empty);
                            toolStates[index] = state;
                        }

                        if (toolCall.TryGetProperty("id", out var idElement) && !string.IsNullOrWhiteSpace(idElement.GetString()))
                        {
                            state.Id = idElement.GetString()!;
                        }

                        if (toolCall.TryGetProperty("function", out var functionElement))
                        {
                            if (functionElement.TryGetProperty("name", out var nameElement) && !string.IsNullOrWhiteSpace(nameElement.GetString()))
                            {
                                state.Name = nameElement.GetString()!;
                            }

                            if (!state.Started && !string.IsNullOrWhiteSpace(state.Name))
                            {
                                state.Started = true;
                                events.Add(new ToolUseStartedEvent(state.Id, state.Name));
                            }

                            if (functionElement.TryGetProperty("arguments", out var argumentsElement) && argumentsElement.ValueKind == JsonValueKind.String)
                            {
                                var partial = argumentsElement.GetString() ?? string.Empty;
                                if (!string.IsNullOrEmpty(partial))
                                {
                                    state.Arguments.Append(partial);
                                    events.Add(new ToolUseInputDeltaEvent(state.Id, state.Name, partial));
                                }
                            }
                        }
                    }
                }
            }

            if (!choice.TryGetProperty("finish_reason", out var finishReasonElement) || finishReasonElement.ValueKind == JsonValueKind.Null)
            {
                continue;
            }

            stopReason = finishReasonElement.GetString() ?? stopReason;
        }

        return new ParsedStreamPayload(events, stopReason);
    }

    private static string NormalizeRole(string role)
        => string.Equals(role, "assistant", StringComparison.OrdinalIgnoreCase) ? "assistant" : "user";

    private static JsonNode? ParseJson(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(value);
        }
        catch
        {
            return null;
        }
    }

    private static string NormalizeToolArguments(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return "{}";
        }

        try
        {
            JsonNode.Parse(content);
            return content;
        }
        catch
        {
            return "{}";
        }
    }

    private sealed class StreamingToolState
    {
        public StreamingToolState(string id, string name)
        {
            Id = id;
            Name = name;
        }

        public string Id { get; set; }

        public string Name { get; set; }

        public bool Started { get; set; }

        public StringBuilder Arguments { get; } = new();
    }

    private sealed record ParsedStreamPayload(IReadOnlyList<ModelStreamEvent> Events, string? StopReason);
}
