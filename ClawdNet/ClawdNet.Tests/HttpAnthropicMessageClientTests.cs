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
