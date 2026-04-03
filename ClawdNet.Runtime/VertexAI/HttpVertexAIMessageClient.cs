using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Exceptions;
using ClawdNet.Core.Models;

namespace ClawdNet.Runtime.VertexAI;

/// <summary>
/// Google Vertex AI Anthropic client implementing IModelClient.
/// Uses Vertex AI rawPredict endpoint with Anthropic-compatible messages API format.
/// Supports SSE streaming with buffered fallback and GCP service account authentication.
/// </summary>
public sealed class HttpVertexAIMessageClient : IModelClient
{
    private readonly HttpClient _httpClient;
    private readonly VertexAICredentialResolver _credentials;
    private readonly string _modelName;

    public HttpVertexAIMessageClient(HttpClient httpClient, string modelName, VertexAICredentialResolver? credentials = null)
    {
        _httpClient = httpClient;
        _modelName = modelName;
        _credentials = credentials ?? new VertexAICredentialResolver();

        if (!_credentials.HasCredentials)
        {
            throw new ModelProviderConfigurationException("vertex",
                "Vertex AI credentials are not set. Set GOOGLE_APPLICATION_CREDENTIALS to a service account key file, " +
                "or set CLAUDE_CODE_SKIP_VERTEX_AUTH=1 for development.");
        }

        if (!_credentials.UseSkipAuth && string.IsNullOrWhiteSpace(_credentials.ProjectId))
        {
            throw new ModelProviderConfigurationException("vertex",
                "Vertex AI project ID is not set. Set ANTHROPIC_VERTEX_PROJECT_ID, GOOGLE_CLOUD_PROJECT, or GCLOUD_PROJECT.");
        }
    }

    public async Task<ModelResponse> SendAsync(ModelRequest request, CancellationToken cancellationToken)
    {
        var resolvedModel = VertexAIModelIdResolver.Resolve(_modelName);
        var region = _credentials.GetRegionForModel(resolvedModel);
        var endpoint = BuildEndpoint(region, _credentials.ProjectId, resolvedModel);

        var payload = BuildPayload(request, stream: false);
        var requestBody = payload.ToJsonString();

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint);
        httpRequest.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");

        await ApplyAuthHeadersAsync(httpRequest, requestBody, cancellationToken);

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new ModelProviderConfigurationException("vertex",
                $"API request failed with {(int)response.StatusCode}: {responseText}");
        }

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        return ParseBufferedResponse(responseJson);
    }

    public async IAsyncEnumerable<ModelStreamEvent> StreamAsync(
        ModelRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var resolvedModel = VertexAIModelIdResolver.Resolve(_modelName);
        var region = _credentials.GetRegionForModel(resolvedModel);
        var endpoint = BuildStreamingEndpoint(region, _credentials.ProjectId, resolvedModel);

        var payload = BuildPayload(request, stream: true);
        var requestBody = payload.ToJsonString();

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint);
        httpRequest.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");
        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        await ApplyAuthHeadersAsync(httpRequest, requestBody, cancellationToken);

        using var response = await _httpClient.SendAsync(
            httpRequest,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new ModelProviderConfigurationException("vertex",
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

    private static string BuildEndpoint(string region, string projectId, string modelId)
    {
        return $"https://{region}-aiplatform.googleapis.com/v1/projects/{projectId}/locations/{region}/publishers/anthropic/models/{modelId}:rawPredict";
    }

    private static string BuildStreamingEndpoint(string region, string projectId, string modelId)
    {
        return $"https://{region}-aiplatform.googleapis.com/v1/projects/{projectId}/locations/{region}/publishers/anthropic/models/{modelId}:streamGenerateContent?alt=sse";
    }

    private async Task ApplyAuthHeadersAsync(HttpRequestMessage request, string requestBody, CancellationToken cancellationToken)
    {
        if (_credentials.UseSkipAuth)
        {
            return;
        }

        // GCP service account JWT -> access token flow
        var saKey = _credentials.LoadServiceAccountKey();
        if (saKey is null)
        {
            throw new ModelProviderConfigurationException("vertex",
                "Service account key file not found or invalid. Set GOOGLE_APPLICATION_CREDENTIALS to a valid key file path.");
        }

        var (clientEmail, privateKey) = saKey.Value;
        var accessToken = await GetAccessTokenAsync(clientEmail, privateKey, cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
    }

    private async Task<string> GetAccessTokenAsync(string clientEmail, string privateKeyPem, CancellationToken cancellationToken)
    {
        // Build JWT assertion
        var now = DateTimeOffset.UtcNow;
        var expiry = now.AddMinutes(60);
        var issuedAt = now.ToUnixTimeSeconds();
        var expiresAt = expiry.ToUnixTimeSeconds();

        // RS256 header
        var header = new JsonObject
        {
            ["alg"] = "RS256",
            ["typ"] = "JWT"
        };

        // JWT claims
        var claimSet = new JsonObject
        {
            ["iss"] = clientEmail,
            ["scope"] = "https://www.googleapis.com/auth/cloud-platform",
            ["aud"] = "https://oauth2.googleapis.com/token",
            ["exp"] = expiresAt,
            ["iat"] = issuedAt
        };

        // Build JWT: header.claimSet (base64url)
        var headerJson = header.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
        var claimJson = claimSet.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
        var headerBase64 = Base64UrlEncode(Encoding.UTF8.GetBytes(headerJson));
        var claimBase64 = Base64UrlEncode(Encoding.UTF8.GetBytes(claimJson));
        var signingInput = $"{headerBase64}.{claimBase64}";

        // Sign with RSA-SHA256
        var signature = SignRsaSha256(privateKeyPem, signingInput);
        var jwt = $"{signingInput}.{Base64UrlEncode(signature)}";

        // Exchange JWT for access token
        using var tokenRequest = new HttpRequestMessage(HttpMethod.Post, "https://oauth2.googleapis.com/token");
        tokenRequest.Content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "urn:ietf:params:oauth:grant-type:jwt-bearer"),
            new KeyValuePair<string, string>("assertion", jwt)
        });

        using var tokenResponse = await _httpClient.SendAsync(tokenRequest, cancellationToken);
        if (!tokenResponse.IsSuccessStatusCode)
        {
            var errorText = await tokenResponse.Content.ReadAsStringAsync(cancellationToken);
            throw new ModelProviderConfigurationException("vertex",
                $"Failed to obtain GCP access token: {errorText}");
        }

        using var tokenDoc = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync(cancellationToken));
        return tokenDoc.RootElement.GetProperty("access_token").GetString()
            ?? throw new ModelProviderConfigurationException("vertex", "GCP token response missing access_token.");
    }

    private static byte[] SignRsaSha256(string privateKeyPem, string data)
    {
        using var rsa = RSA.Create();
        // Strip PEM headers/footers and decode
        var pemLines = privateKeyPem
            .Replace("-----BEGIN PRIVATE KEY-----", "")
            .Replace("-----END PRIVATE KEY-----", "")
            .Replace("-----BEGIN RSA PRIVATE KEY-----", "")
            .Replace("-----END RSA PRIVATE KEY-----", "")
            .Replace("\r", "")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var keyBytes = Convert.FromBase64String(string.Concat(pemLines));

        // Import PKCS#8 or PKCS#1 private key
        rsa.ImportPkcs8PrivateKey(keyBytes, out _);

        return rsa.SignData(Encoding.UTF8.GetBytes(data), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    }

    private static string Base64UrlEncode(byte[] data)
    {
        return Convert.ToBase64String(data)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
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
                yield return new MessageStartedEvent(
                    root.TryGetProperty("message", out var msgEl) && msgEl.TryGetProperty("model", out var modelEl)
                        ? modelEl.GetString() ?? string.Empty
                        : string.Empty);
                break;
            case "content_block_delta":
                var delta = root.GetProperty("delta");
                var deltaType = delta.TryGetProperty("type", out var dtEl) ? dtEl.GetString() : null;
                if (string.Equals(deltaType, "text_delta", StringComparison.Ordinal))
                {
                    yield return new TextDeltaEvent(delta.GetProperty("text").GetString() ?? string.Empty);
                }
                else if (string.Equals(deltaType, "input_json_delta", StringComparison.Ordinal))
                {
                    var name = root.TryGetProperty("content_block", out var blockEl) &&
                               blockEl.TryGetProperty("name", out var blockName)
                        ? blockName.GetString() ?? string.Empty
                        : string.Empty;
                    yield return new ToolUseInputDeltaEvent(
                        root.TryGetProperty("content_block", out var cbEl) && cbEl.TryGetProperty("id", out var idEl)
                            ? idEl.GetString() ?? Guid.NewGuid().ToString("N")
                            : Guid.NewGuid().ToString("N"),
                        name,
                        delta.TryGetProperty("partial_json", out var pjEl) ? pjEl.GetString() ?? string.Empty : string.Empty);
                }
                break;
            case "content_block_start":
                var contentBlock = root.TryGetProperty("content_block", out var cb) ? cb : default;
                if (contentBlock.ValueKind != JsonValueKind.Undefined &&
                    string.Equals(contentBlock.GetProperty("type").GetString(), "tool_use", StringComparison.Ordinal))
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
                yield return new ModelErrorEvent(
                    root.TryGetProperty("error", out var errorElement)
                        ? errorElement.TryGetProperty("message", out var msgEl2)
                            ? msgEl2.GetString() ?? "Vertex AI streaming error."
                            : "Vertex AI streaming error."
                        : "Vertex AI streaming error.");
                break;
        }
    }
}
