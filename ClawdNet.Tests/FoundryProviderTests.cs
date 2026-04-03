using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using ClawdNet.Core.Models;
using ClawdNet.Runtime.Foundry;

namespace ClawdNet.Tests;

public sealed class FoundryCredentialResolverTests
{
    [Fact]
    public void HasCredentials_returns_true_for_api_key()
    {
        var resolver = new FoundryCredentialResolver(
            apiKey: "my-foundry-api-key",
            skipAuth: false,
            resourceName: "my-resource",
            customBaseUrl: null);

        Assert.True(resolver.HasCredentials);
        Assert.False(resolver.UseSkipAuth);
        Assert.Equal("my-foundry-api-key", resolver.GetApiKey());
    }

    [Fact]
    public void HasCredentials_returns_true_for_skip_auth()
    {
        var resolver = new FoundryCredentialResolver(
            apiKey: null,
            skipAuth: true,
            resourceName: "my-resource",
            customBaseUrl: null);

        Assert.True(resolver.HasCredentials);
        Assert.True(resolver.UseSkipAuth);
    }

    [Fact]
    public void HasCredentials_returns_false_when_no_credentials()
    {
        var resolver = new FoundryCredentialResolver(
            apiKey: null,
            skipAuth: false,
            resourceName: null,
            customBaseUrl: null);

        Assert.False(resolver.HasCredentials);
    }

    [Fact]
    public void BuildBaseUrl_uses_resource_name()
    {
        var resolver = new FoundryCredentialResolver(
            apiKey: "my-key",
            skipAuth: false,
            resourceName: "my-resource",
            customBaseUrl: null);

        var url = resolver.BuildBaseUrl();
        Assert.Equal("https://my-resource.services.ai.azure.com/anthropic", url);
    }

    [Fact]
    public void BuildBaseUrl_uses_custom_base_url()
    {
        var resolver = new FoundryCredentialResolver(
            apiKey: "my-key",
            skipAuth: false,
            resourceName: "my-resource",
            customBaseUrl: "https://custom.example.com/anthropic");

        var url = resolver.BuildBaseUrl();
        Assert.Equal("https://custom.example.com/anthropic", url);
    }

    [Fact]
    public void BuildBaseUrl_returns_empty_when_nothing_set()
    {
        var resolver = new FoundryCredentialResolver(
            apiKey: null,
            skipAuth: true,
            resourceName: null,
            customBaseUrl: null);

        var url = resolver.BuildBaseUrl();
        Assert.Equal(string.Empty, url);
    }

    [Fact]
    public void BuildBaseUrl_trims_trailing_slash()
    {
        var resolver = new FoundryCredentialResolver(
            apiKey: "my-key",
            skipAuth: false,
            resourceName: null,
            customBaseUrl: "https://custom.example.com/anthropic/");

        var url = resolver.BuildBaseUrl();
        Assert.Equal("https://custom.example.com/anthropic", url);
    }
}

public sealed class HttpFoundryMessageClientTests
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
              "model": "claude-sonnet-4-5",
              "content": [
                {
                  "type": "text",
                  "text": "hello from foundry"
                }
              ],
              "stop_reason": "end_turn"
            }
            """;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        });

        var credentials = new FoundryCredentialResolver(
            apiKey: null,
            skipAuth: true,
            resourceName: "my-resource",
            customBaseUrl: null);

        var client = new HttpFoundryMessageClient(
            new HttpClient(handler),
            "claude-sonnet-4-5",
            credentials);

        var response = await client.SendAsync(
            new ModelRequest(
                "claude-sonnet-4-5",
                "system prompt",
                [new ModelMessage("user", "hi")],
                [new ToolDefinition("echo", "Echo text", new JsonObject { ["type"] = "object" })]),
            CancellationToken.None);

        Assert.NotNull(capturedRequest);
        Assert.Contains(response.ContentBlocks, block => block is TextContentBlock text && text.Text == "hello from foundry");
        Assert.Equal("end_turn", response.StopReason);
    }

    [Fact]
    public void Client_throws_when_no_credentials()
    {
        var credentials = new FoundryCredentialResolver(
            apiKey: null,
            skipAuth: false,
            resourceName: null,
            customBaseUrl: null);

        var ex = Assert.Throws<ClawdNet.Core.Exceptions.ModelProviderConfigurationException>(() =>
            new HttpFoundryMessageClient(new HttpClient(), "claude-sonnet-4-5", credentials));

        Assert.Contains("foundry", ex.Message);
    }

    [Fact]
    public void Client_throws_when_no_base_url()
    {
        var credentials = new FoundryCredentialResolver(
            apiKey: null,
            skipAuth: true,
            resourceName: null,
            customBaseUrl: null);

        var ex = Assert.Throws<ClawdNet.Core.Exceptions.ModelProviderConfigurationException>(() =>
            new HttpFoundryMessageClient(new HttpClient(), "claude-sonnet-4-5", credentials));

        Assert.Contains("base URL", ex.Message);
    }

    [Fact]
    public async Task Client_builds_correct_endpoint_url_with_resource()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new DelegatingHandlerStub(async request =>
        {
            capturedRequest = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                {
                  "model": "claude-sonnet-4-5",
                  "content": [{"type": "text", "text": "ok"}],
                  "stop_reason": "end_turn"
                }
                """, Encoding.UTF8, "application/json")
            };
        });

        var credentials = new FoundryCredentialResolver(
            apiKey: null,
            skipAuth: true,
            resourceName: "my-deployment",
            customBaseUrl: null);

        var client = new HttpFoundryMessageClient(
            new HttpClient(handler),
            "claude-sonnet-4-5",
            credentials);

        await client.SendAsync(
            new ModelRequest(
                "claude-sonnet-4-5",
                "system",
                [new ModelMessage("user", "hi")],
                []),
            CancellationToken.None);

        Assert.NotNull(capturedRequest);
        Assert.Equal("POST", capturedRequest!.Method!.ToString());
        Assert.Contains("my-deployment.services.ai.azure.com", capturedRequest.RequestUri!.ToString());
        Assert.Contains("/anthropic/v1/messages", capturedRequest.RequestUri.ToString());
    }

    [Fact]
    public async Task Client_sends_api_key_header_when_not_skipping_auth()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new DelegatingHandlerStub(async request =>
        {
            capturedRequest = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                {
                  "model": "claude-sonnet-4-5",
                  "content": [{"type": "text", "text": "ok"}],
                  "stop_reason": "end_turn"
                }
                """, Encoding.UTF8, "application/json")
            };
        });

        var credentials = new FoundryCredentialResolver(
            apiKey: "test-api-key-123",
            skipAuth: false,
            resourceName: "my-resource",
            customBaseUrl: null);

        var client = new HttpFoundryMessageClient(
            new HttpClient(handler),
            "claude-sonnet-4-5",
            credentials);

        await client.SendAsync(
            new ModelRequest(
                "claude-sonnet-4-5",
                "system",
                [new ModelMessage("user", "hi")],
                []),
            CancellationToken.None);

        Assert.NotNull(capturedRequest);
        Assert.True(capturedRequest!.Headers.Contains("api-key"));
        Assert.Equal("test-api-key-123", capturedRequest.Headers.GetValues("api-key").First());
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
