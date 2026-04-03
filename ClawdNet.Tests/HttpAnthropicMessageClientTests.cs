using System.Net;
using System.Net.Http;
using System.Text;
using ClawdNet.Core.Models;
using ClawdNet.Runtime.Anthropic;

namespace ClawdNet.Tests;

public sealed class HttpAnthropicMessageClientTests
{
    [Fact]
    public async Task Client_sends_expected_payload_and_parses_text_response()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new DelegatingHandlerStub(async request =>
        {
            capturedRequest = request;
            var json = """
            {
              "model":"claude-sonnet-4-5",
              "stop_reason":"end_turn",
              "content":[{"type":"text","text":"hello"}]
            }
            """;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        });
        var client = new HttpAnthropicMessageClient(new HttpClient(handler), () => "test-key", "https://api.example.com");

        var response = await client.SendAsync(
            new ModelRequest(
                "claude-sonnet-4-5",
                "system prompt",
                [new ModelMessage("user", "hi")],
                []),
            CancellationToken.None);

        Assert.NotNull(capturedRequest);
        Assert.Equal("https://api.example.com/v1/messages", capturedRequest!.RequestUri!.ToString());
        Assert.Equal("hello", ((TextContentBlock)response.ContentBlocks.Single()).Text);
    }

    [Fact]
    public async Task Client_streams_sse_text_and_tool_events()
    {
        var handler = new DelegatingHandlerStub(_ =>
        {
            var sse = """
            event: message_start
            data: {"message":{"model":"claude-sonnet-4-5"}}

            event: content_block_delta
            data: {"delta":{"type":"text_delta","text":"hel"}}

            event: content_block_delta
            data: {"delta":{"type":"text_delta","text":"lo"}}

            event: content_block_stop
            data: {"index":0,"content_block":{"type":"text","text":"hello"}}

            event: content_block_start
            data: {"content_block":{"type":"tool_use","id":"tool-1","name":"echo"}}

            event: content_block_delta
            data: {"delta":{"type":"input_json_delta","partial_json":"{\"text\":\"hi\"}"}}

            event: content_block_stop
            data: {"index":1,"content_block":{"type":"tool_use","id":"tool-1","name":"echo","input":{"text":"hi"}}}

            event: message_delta
            data: {"delta":{"stop_reason":"tool_use"}}

            """;

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(sse, Encoding.UTF8, "text/event-stream")
            });
        });
        var client = new HttpAnthropicMessageClient(new HttpClient(handler), () => "test-key", "https://api.example.com");

        var events = new List<ModelStreamEvent>();
        await foreach (var streamEvent in client.StreamAsync(
                           new ModelRequest("claude-sonnet-4-5", "system", [new ModelMessage("user", "hi")], []),
                           CancellationToken.None))
        {
            events.Add(streamEvent);
        }

        Assert.Contains(events, streamEvent => streamEvent is MessageStartedEvent started && started.Model == "claude-sonnet-4-5");
        Assert.Equal(2, events.Count(streamEvent => streamEvent is TextDeltaEvent));
        Assert.Contains(events, streamEvent => streamEvent is TextCompletedEvent completed && completed.Text == "hello");
        Assert.Contains(events, streamEvent => streamEvent is ToolUseStartedEvent toolStarted && toolStarted.Name == "echo");
        Assert.Contains(events, streamEvent => streamEvent is ToolUseCompletedEvent toolCompleted && toolCompleted.Name == "echo");
        Assert.Contains(events, streamEvent => streamEvent is MessageCompletedEvent completed && completed.StopReason == "tool_use");
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
