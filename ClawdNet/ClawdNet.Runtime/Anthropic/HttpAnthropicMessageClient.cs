using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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
            throw new AnthropicConfigurationException("ANTHROPIC_API_KEY is not set.");
        }

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/v1/messages");
        httpRequest.Headers.Add("x-api-key", apiKey);
        httpRequest.Headers.Add("anthropic-version", "2023-06-01");
        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var payload = BuildPayload(request);
        httpRequest.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new AnthropicConfigurationException(
                $"Anthropic API request failed with {(int)response.StatusCode}: {responseText}");
        }

        return ParseResponse(responseText);
    }

    private static JsonObject BuildPayload(ModelRequest request)
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
            ["tools"] = tools
        };
    }

    private static ModelResponse ParseResponse(string responseText)
    {
        using var document = JsonDocument.Parse(responseText);
        var root = document.RootElement;
        var blocks = new List<ModelContentBlock>();

        foreach (var contentElement in root.GetProperty("content").EnumerateArray())
        {
            var type = contentElement.GetProperty("type").GetString();
            switch (type)
            {
                case "text":
                    blocks.Add(new TextContentBlock(contentElement.GetProperty("text").GetString() ?? string.Empty));
                    break;
                case "tool_use":
                    var inputText = contentElement.GetProperty("input").GetRawText();
                    blocks.Add(new ToolUseContentBlock(
                        contentElement.GetProperty("id").GetString() ?? Guid.NewGuid().ToString("N"),
                        contentElement.GetProperty("name").GetString() ?? string.Empty,
                        JsonNode.Parse(inputText)));
                    break;
            }
        }

        return new ModelResponse(
            root.GetProperty("model").GetString() ?? string.Empty,
            blocks,
            root.TryGetProperty("stop_reason", out var stopReason) ? stopReason.GetString() ?? string.Empty : string.Empty);
    }
}
