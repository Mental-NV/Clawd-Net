using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClawdNet.Core.Models;
using ClawdNet.Runtime.VertexAI;

namespace ClawdNet.Tests;

public sealed class VertexAICredentialResolverTests
{
    [Fact]
    public void HasCredentials_returns_true_for_service_account_key()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var resolver = new VertexAICredentialResolver(
                serviceAccountKeyPath: tempFile,
                projectId: "my-project",
                skipAuth: false,
                region: "us-east5");

            Assert.True(resolver.HasCredentials);
            Assert.False(resolver.UseSkipAuth);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void HasCredentials_returns_true_for_skip_auth()
    {
        var resolver = new VertexAICredentialResolver(
            serviceAccountKeyPath: null,
            projectId: "my-project",
            skipAuth: true,
            region: "us-east5");

        Assert.True(resolver.HasCredentials);
        Assert.True(resolver.UseSkipAuth);
    }

    [Fact]
    public void HasCredentials_returns_false_when_no_credentials()
    {
        var resolver = new VertexAICredentialResolver(
            serviceAccountKeyPath: null,
            projectId: null,
            skipAuth: false,
            region: "us-east5");

        Assert.False(resolver.HasCredentials);
    }

    [Fact]
    public void HasCredentials_returns_false_for_nonexistent_key_path()
    {
        var resolver = new VertexAICredentialResolver(
            serviceAccountKeyPath: "/nonexistent/path",
            projectId: "my-project",
            skipAuth: false,
            region: "us-east5");

        Assert.False(resolver.HasCredentials);
    }

    [Fact]
    public void Region_defaults_to_us_east5()
    {
        var resolver = new VertexAICredentialResolver(
            serviceAccountKeyPath: null,
            projectId: "my-project",
            skipAuth: true,
            region: null);

        Assert.Equal("us-east5", resolver.Region);
    }

    [Fact]
    public void Region_uses_provided_value()
    {
        var resolver = new VertexAICredentialResolver(
            serviceAccountKeyPath: null,
            projectId: "my-project",
            skipAuth: true,
            region: "europe-west4");

        Assert.Equal("europe-west4", resolver.Region);
    }

    [Fact]
    public void GetRegionForModel_returns_default_when_no_match()
    {
        var resolver = new VertexAICredentialResolver(
            serviceAccountKeyPath: null,
            projectId: "my-project",
            skipAuth: true,
            region: "us-east5");

        Assert.Equal("us-east5", resolver.GetRegionForModel("unknown-model"));
    }

    [Fact]
    public void GetRegionForModel_returns_model_override_env_var()
    {
        Environment.SetEnvironmentVariable("VERTEX_REGION_CLAUDE_4_5_SONNET", "europe-west1");
        try
        {
            var resolver = new VertexAICredentialResolver(
                serviceAccountKeyPath: null,
                projectId: "my-project",
                skipAuth: true,
                region: "us-east5");

            Assert.Equal("europe-west1", resolver.GetRegionForModel("claude-sonnet-4-5"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("VERTEX_REGION_CLAUDE_4_5_SONNET", null);
        }
    }

    [Fact]
    public void LoadServiceAccountKey_returns_null_for_missing_file()
    {
        var resolver = new VertexAICredentialResolver(
            serviceAccountKeyPath: "/nonexistent/key.json",
            projectId: "my-project",
            skipAuth: false,
            region: "us-east5");

        Assert.Null(resolver.LoadServiceAccountKey());
    }

    [Fact]
    public void LoadServiceAccountKey_parses_valid_json()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var json = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["client_email"] = "test@project.iam.gserviceaccount.com",
                ["private_key"] = "-----BEGIN RSA PRIVATE KEY-----\ntest\n-----END RSA PRIVATE KEY-----"
            });
            File.WriteAllText(tempFile, json);

            var resolver = new VertexAICredentialResolver(
                serviceAccountKeyPath: tempFile,
                projectId: "my-project",
                skipAuth: false,
                region: "us-east5");

            var result = resolver.LoadServiceAccountKey();
            Assert.NotNull(result);
            Assert.Equal("test@project.iam.gserviceaccount.com", result.Value.ClientEmail);
            Assert.Contains("test", result.Value.PrivateKey);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}

public sealed class VertexAIModelIdResolverTests
{
    [Fact]
    public void Resolve_converts_short_name_to_vertex_format()
    {
        Assert.Equal("claude-sonnet-4-5@20250929", VertexAIModelIdResolver.Resolve("claude-sonnet-4-5"));
        Assert.Equal("claude-sonnet-4@20250514", VertexAIModelIdResolver.Resolve("claude-sonnet-4"));
        Assert.Equal("claude-opus-4@20250514", VertexAIModelIdResolver.Resolve("claude-opus-4"));
        Assert.Equal("claude-opus-4-1@20250805", VertexAIModelIdResolver.Resolve("claude-opus-4-1"));
        Assert.Equal("claude-3-5-haiku@20241022", VertexAIModelIdResolver.Resolve("claude-3-5-haiku"));
    }

    [Fact]
    public void Resolve_passes_through_already_in_vertex_format()
    {
        Assert.Equal("claude-sonnet-4-5@20250929", VertexAIModelIdResolver.Resolve("claude-sonnet-4-5@20250929"));
        Assert.Equal("custom-model@20260101", VertexAIModelIdResolver.Resolve("custom-model@20260101"));
    }

    [Fact]
    public void Resolve_returns_unknown_model_as_is()
    {
        Assert.Equal("unknown-model", VertexAIModelIdResolver.Resolve("unknown-model"));
    }

    [Fact]
    public void Resolve_handles_null()
    {
        Assert.Null(VertexAIModelIdResolver.Resolve(null!));
    }

    [Fact]
    public void Resolve_handles_empty_string()
    {
        Assert.Equal(string.Empty, VertexAIModelIdResolver.Resolve(string.Empty));
    }
}

public sealed class HttpVertexAIMessageClientTests
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
              "model": "claude-sonnet-4-5@20250929",
              "content": [
                {
                  "type": "text",
                  "text": "hello from vertex ai"
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

        var credentials = new VertexAICredentialResolver(
            serviceAccountKeyPath: null,
            projectId: "my-project",
            skipAuth: true,
            region: "us-east5");

        var client = new HttpVertexAIMessageClient(
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
        Assert.Contains(response.ContentBlocks, block => block is TextContentBlock text && text.Text == "hello from vertex ai");
        Assert.Equal("end_turn", response.StopReason);
    }

    [Fact]
    public void Client_throws_when_no_credentials()
    {
        var credentials = new VertexAICredentialResolver(
            serviceAccountKeyPath: null,
            projectId: null,
            skipAuth: false,
            region: "us-east5");

        var ex = Assert.Throws<ClawdNet.Core.Exceptions.ModelProviderConfigurationException>(() =>
            new HttpVertexAIMessageClient(new HttpClient(), "claude-sonnet-4-5", credentials));

        Assert.Contains("vertex", ex.Message);
    }

    [Fact]
    public void Client_throws_when_no_project_id()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var credentials = new VertexAICredentialResolver(
                serviceAccountKeyPath: tempFile,
                projectId: null,
                skipAuth: false,
                region: "us-east5");

            var ex = Assert.Throws<ClawdNet.Core.Exceptions.ModelProviderConfigurationException>(() =>
                new HttpVertexAIMessageClient(new HttpClient(), "claude-sonnet-4-5", credentials));

            Assert.Contains("project", ex.Message);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task Client_builds_correct_endpoint_url()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new DelegatingHandlerStub(async request =>
        {
            capturedRequest = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                {
                  "model": "claude-sonnet-4-5@20250929",
                  "content": [{"type": "text", "text": "ok"}],
                  "stop_reason": "end_turn"
                }
                """, Encoding.UTF8, "application/json")
            };
        });

        var credentials = new VertexAICredentialResolver(
            serviceAccountKeyPath: null,
            projectId: "my-project",
            skipAuth: true,
            region: "us-east5");

        var client = new HttpVertexAIMessageClient(
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
        Assert.Contains("us-east5-aiplatform.googleapis.com", capturedRequest!.RequestUri!.ToString());
        Assert.Contains("my-project", capturedRequest.RequestUri.ToString());
        Assert.Contains("claude-sonnet-4-5@20250929", capturedRequest.RequestUri.ToString());
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
