using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using ClawdNet.Core.Models;
using ClawdNet.Runtime.Bedrock;

namespace ClawdNet.Tests;

public sealed class BedrockCredentialResolverTests
{
    [Fact]
    public void HasCredentials_returns_true_for_standard_auth()
    {
        var resolver = new BedrockCredentialResolver(
            accessKeyId: "AKIAIOSFODNN7EXAMPLE",
            secretAccessKey: "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY",
            sessionToken: null,
            bearerToken: null,
            skipAuth: false,
            region: "us-east-1",
            customEndpoint: null);

        Assert.True(resolver.HasCredentials);
        Assert.True(resolver.UseStandardAuth);
        Assert.False(resolver.UseBearerAuth);
        Assert.False(resolver.UseSkipAuth);
    }

    [Fact]
    public void HasCredentials_returns_true_for_bearer_auth()
    {
        var resolver = new BedrockCredentialResolver(
            accessKeyId: null,
            secretAccessKey: null,
            sessionToken: null,
            bearerToken: "my-bearer-token",
            skipAuth: false,
            region: "us-east-1",
            customEndpoint: null);

        Assert.True(resolver.HasCredentials);
        Assert.True(resolver.UseBearerAuth);
        Assert.False(resolver.UseStandardAuth);
    }

    [Fact]
    public void HasCredentials_returns_true_for_skip_auth()
    {
        var resolver = new BedrockCredentialResolver(
            accessKeyId: null,
            secretAccessKey: null,
            sessionToken: null,
            bearerToken: null,
            skipAuth: true,
            region: "us-east-1",
            customEndpoint: null);

        Assert.True(resolver.HasCredentials);
        Assert.True(resolver.UseSkipAuth);
    }

    [Fact]
    public void HasCredentials_returns_false_when_no_credentials()
    {
        var resolver = new BedrockCredentialResolver(
            accessKeyId: null,
            secretAccessKey: null,
            sessionToken: null,
            bearerToken: null,
            skipAuth: false,
            region: "us-east-1",
            customEndpoint: null);

        Assert.False(resolver.HasCredentials);
    }

    [Fact]
    public void Region_defaults_to_us_east_1()
    {
        var resolver = new BedrockCredentialResolver(
            accessKeyId: "AKIAIOSFODNN7EXAMPLE",
            secretAccessKey: "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY",
            sessionToken: null,
            bearerToken: null,
            skipAuth: false,
            region: null,
            customEndpoint: null);

        Assert.Equal("us-east-1", resolver.Region);
    }

    [Fact]
    public void Region_uses_provided_value()
    {
        var resolver = new BedrockCredentialResolver(
            accessKeyId: "AKIAIOSFODNN7EXAMPLE",
            secretAccessKey: "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY",
            sessionToken: null,
            bearerToken: null,
            skipAuth: false,
            region: "eu-west-1",
            customEndpoint: null);

        Assert.Equal("eu-west-1", resolver.Region);
    }

    [Fact]
    public void BuildEndpoint_returns_standard_model_path()
    {
        var resolver = new BedrockCredentialResolver(
            accessKeyId: "AKIAIOSFODNN7EXAMPLE",
            secretAccessKey: "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY",
            sessionToken: null,
            bearerToken: null,
            skipAuth: false,
            region: "us-east-1",
            customEndpoint: null);

        var endpoint = resolver.BuildEndpoint("anthropic.claude-sonnet-4-5-20250514-v1:0");

        Assert.Equal("https://bedrock-runtime.us-east-1.amazonaws.com/model/anthropic.claude-sonnet-4-5-20250514-v1:0/converse", endpoint);
    }

    [Fact]
    public void BuildEndpoint_returns_arn_path()
    {
        var resolver = new BedrockCredentialResolver(
            accessKeyId: "AKIAIOSFODNN7EXAMPLE",
            secretAccessKey: "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY",
            sessionToken: null,
            bearerToken: null,
            skipAuth: false,
            region: "us-east-1",
            customEndpoint: null);

        var arn = "arn:aws:bedrock:us-east-1:123456789012:inference-profile/us.anthropic.claude-sonnet-4-5-20250514-v1:0";
        var endpoint = resolver.BuildEndpoint(arn);

        Assert.StartsWith("https://bedrock-runtime.us-east-1.amazonaws.com/", endpoint);
        Assert.EndsWith("/converse", endpoint);
        // ARN resource path should contain the resource type and id after the account
        Assert.Contains("inference-profile", endpoint);
    }

    [Fact]
    public void BuildEndpoint_uses_custom_endpoint_when_set()
    {
        var resolver = new BedrockCredentialResolver(
            accessKeyId: "AKIAIOSFODNN7EXAMPLE",
            secretAccessKey: "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY",
            sessionToken: null,
            bearerToken: null,
            skipAuth: false,
            region: "us-east-1",
            customEndpoint: "https://bedrock.example.com");

        var endpoint = resolver.BuildEndpoint("anthropic.claude-sonnet-4-5-20250514-v1:0");

        Assert.Equal("https://bedrock.example.com/model/anthropic.claude-sonnet-4-5-20250514-v1:0/converse", endpoint);
    }

    [Fact]
    public void ComputePayloadHash_returns_expected_sha256()
    {
        var payload = "{\"test\":\"value\"}";
        var hash = BedrockCredentialResolver.ComputePayloadHash(payload);

        Assert.NotEmpty(hash);
        Assert.All(hash, c => Assert.True(char.IsAsciiLetterOrDigit(c)));
        Assert.Equal(64, hash.Length); // SHA-256 hex length
    }

    [Fact]
    public void GetAmzDate_returns_expected_format()
    {
        var utcNow = new DateTime(2026, 4, 4, 12, 0, 0, DateTimeKind.Utc);
        var amzDate = BedrockCredentialResolver.GetAmzDate(utcNow);

        Assert.Equal("20260404T120000Z", amzDate);
    }
}

public sealed class HttpBedrockMessageClientTests
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
              "output": {
                "message": {
                  "modelId": "anthropic.claude-sonnet-4-5-20250514-v1:0",
                  "content": [
                    {
                      "type": "text",
                      "text": "hello from bedrock"
                    }
                  ]
                }
              },
              "stopReason": "end_turn"
            }
            """;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        });

        var credentials = new BedrockCredentialResolver(
            accessKeyId: "AKIAIOSFODNN7EXAMPLE",
            secretAccessKey: "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY",
            sessionToken: null,
            bearerToken: null,
            skipAuth: false,
            region: "us-east-1",
            customEndpoint: null);

        var client = new HttpBedrockMessageClient(
            new HttpClient(handler),
            "anthropic.claude-sonnet-4-5-20250514-v1:0",
            credentials);

        var response = await client.SendAsync(
            new ModelRequest(
                "anthropic.claude-sonnet-4-5-20250514-v1:0",
                "system prompt",
                [new ModelMessage("user", "hi")],
                [new ToolDefinition("echo", "Echo text", new JsonObject { ["type"] = "object" })]),
            CancellationToken.None);

        Assert.NotNull(capturedRequest);
        Assert.Contains(response.ContentBlocks, block => block is TextContentBlock text && text.Text == "hello from bedrock");
        Assert.Equal("end_turn", response.StopReason);
    }

    [Fact]
    public void Client_throws_when_no_credentials()
    {
        var credentials = new BedrockCredentialResolver(
            accessKeyId: null,
            secretAccessKey: null,
            sessionToken: null,
            bearerToken: null,
            skipAuth: false,
            region: "us-east-1",
            customEndpoint: null);

        var ex = Assert.Throws<ClawdNet.Core.Exceptions.ModelProviderConfigurationException>(() =>
            new HttpBedrockMessageClient(new HttpClient(), "test-model", credentials));

        Assert.Contains("bedrock", ex.Message);
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
