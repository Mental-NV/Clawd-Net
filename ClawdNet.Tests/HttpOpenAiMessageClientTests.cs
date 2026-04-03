using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using ClawdNet.Core.Models;
using ClawdNet.Runtime.OpenAI;

namespace ClawdNet.Tests;

public sealed class HttpOpenAiMessageClientTests
{
    [Fact]
    public async Task Client_sends_expected_payload_and_parses_text_and_tool_response()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new DelegatingHandlerStub(async request =>
        {
            capturedRequest = request;
            var json = """
            {
              "model":"gpt-4o-mini",
              "choices":[
                {
                  "finish_reason":"tool_calls",
                  "message":{
                    "content":"hello",
                    "tool_calls":[
                      {
                        "id":"call_1",
                        "type":"function",
                        "function":{"name":"echo","arguments":"{\"text\":\"hi\"}"}
                      }
                    ]
                  }
                }
              ]
            }
            """;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        });
        var client = new HttpOpenAiMessageClient(new HttpClient(handler), () => "test-key", "https://api.example.com");

        var response = await client.SendAsync(
            new ModelRequest(
                "gpt-4o-mini",
                "system prompt",
                [new ModelMessage("user", "hi")],
                [new ToolDefinition("echo", "Echo text", new JsonObject { ["type"] = "object" })]),
            CancellationToken.None);

        Assert.NotNull(capturedRequest);
        Assert.Equal("https://api.example.com/v1/chat/completions", capturedRequest!.RequestUri!.ToString());
        Assert.Contains(response.ContentBlocks, block => block is TextContentBlock text && text.Text == "hello");
        Assert.Contains(response.ContentBlocks, block => block is ToolUseContentBlock tool && tool.Name == "echo");
    }

    [Fact]
    public async Task Client_streams_text_and_tool_events()
    {
        var handler = new DelegatingHandlerStub(_ =>
        {
            var sse = """
            data: {"choices":[{"delta":{"content":"hel"},"finish_reason":null}]}

            data: {"choices":[{"delta":{"content":"lo"},"finish_reason":null}]}

            data: {"choices":[{"delta":{"tool_calls":[{"index":0,"id":"call_1","function":{"name":"echo","arguments":"{\"text\":\"hi\"}"}}]},"finish_reason":"tool_calls"}]}

            data: [DONE]
            """;

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(sse, Encoding.UTF8, "text/event-stream")
            });
        });
        var client = new HttpOpenAiMessageClient(new HttpClient(handler), () => "test-key", "https://api.example.com");

        var events = new List<ModelStreamEvent>();
        await foreach (var streamEvent in client.StreamAsync(
                           new ModelRequest("gpt-4o-mini", "system", [new ModelMessage("user", "hi")], []),
                           CancellationToken.None))
        {
            events.Add(streamEvent);
        }

        Assert.Contains(events, streamEvent => streamEvent is MessageStartedEvent started && started.Model == "gpt-4o-mini");
        Assert.Equal(2, events.Count(streamEvent => streamEvent is TextDeltaEvent));
        Assert.Contains(events, streamEvent => streamEvent is TextCompletedEvent completed && completed.Text == "hello");
        Assert.Contains(events, streamEvent => streamEvent is ToolUseStartedEvent started && started.Name == "echo");
        Assert.Contains(events, streamEvent => streamEvent is ToolUseCompletedEvent completed && completed.Name == "echo");
        Assert.Contains(events, streamEvent => streamEvent is MessageCompletedEvent completed && completed.StopReason == "tool_calls");
    }

    private sealed class DelegatingHandlerStub : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _handler;

        public DelegatingHandlerStub(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return _handler(request);
        }
    }
}
