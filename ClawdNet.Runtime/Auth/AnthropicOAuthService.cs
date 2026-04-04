using System.Collections.Specialized;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Web;
using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Models;

namespace ClawdNet.Runtime.Auth;

/// <summary>
/// Anthropic OAuth service implementing browser-based login with PKCE.
/// Uses file-based token storage (ITokenStore) for persistence.
/// </summary>
public sealed class AnthropicOAuthService : IOAuthService, IAsyncDisposable
{
    private const string TokenKey = "anthropic_oauth";
    private const string ClientId = "9d1c250a-e61b-44d9-88ed-5944d1962f5e";
    private const string AuthorizeUrl = "https://console.anthropic.com/oauth2/auth";
    private const string TokenUrl = "https://console.anthropic.com/oauth2/token";
    private const string ProfileUrl = "https://api.anthropic.com/api/oauth/claude_cli/profile";
    private const string RolesUrl = "https://api.anthropic.com/api/oauth/claude_cli/roles";
    private const string ApiKeyCreateUrl = "https://api.anthropic.com/api/oauth/claude_cli/create_api_key";
    private const int DefaultCallbackPort = 9876;
    // 5-minute buffer before actual expiry
    private static readonly TimeSpan TokenExpiryBuffer = TimeSpan.FromMinutes(5);

    private readonly ITokenStore _tokenStore;
    private readonly HttpClient _httpClient;
    private bool _disposed;

    public AnthropicOAuthService(ITokenStore tokenStore, HttpClient? httpClient = null)
    {
        _tokenStore = tokenStore;
        _httpClient = httpClient ?? new HttpClient();
        IsSupported = true;
    }

    public bool IsSupported { get; }

    public async Task<OAuthAccountInfo> LoginAsync(OAuthLoginOptions options, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(AnthropicOAuthService));

        var callbackPort = options.CallbackPort;
        var (codeVerifier, codeChallenge) = GeneratePkce();
        var state = GenerateState();

        // Build authorization URL
        var authorizeUri = new UriBuilder(AuthorizeUrl);
        var queryParams = HttpUtility.ParseQueryString(authorizeUri.Query);
        queryParams["response_type"] = "code";
        queryParams["client_id"] = ClientId;
        queryParams["redirect_uri"] = $"http://localhost:{callbackPort}/callback";
        queryParams["code_challenge"] = codeChallenge;
        queryParams["code_challenge_method"] = "S256";
        queryParams["state"] = state;
        queryParams["scope"] = "user:profile user:sessions:claude_code user:mcp_servers user:file_upload";
        authorizeUri.Query = queryParams.ToString();

        // Open browser
        var url = authorizeUri.ToString();
        try
        {
            OpenBrowser(url);
        }
        catch
        {
            throw new InvalidOperationException(
                $"Could not open browser. Navigate to:\n{url}");
        }

        // Start local callback server
        var authCode = await WaitForCallbackAsync(callbackPort, state, cancellationToken);

        // Exchange code for tokens
        var tokens = await ExchangeCodeForTokensAsync(authCode, codeVerifier, callbackPort, cancellationToken);

        // Store tokens
        await _tokenStore.SaveTokensAsync(TokenKey, tokens, cancellationToken);

        // Fetch account info
        var accountInfo = await FetchAccountInfoAsync(tokens.AccessToken, cancellationToken);

        // Fetch roles for rate limit tier
        await FetchUserRolesAsync(tokens.AccessToken, cancellationToken);

        return accountInfo;
    }

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(AnthropicOAuthService));
        await _tokenStore.DeleteTokensAsync(TokenKey, cancellationToken);
    }

    public async Task<OAuthAccountInfo?> GetAccountInfoAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(AnthropicOAuthService));

        var tokens = await _tokenStore.LoadTokensAsync(TokenKey, cancellationToken);
        if (tokens == null || string.IsNullOrEmpty(tokens.AccessToken))
        {
            return null;
        }

        // Check if token is expired
        if (tokens.ExpiresAt.HasValue && tokens.ExpiresAt.Value <= DateTimeOffset.UtcNow + TokenExpiryBuffer)
        {
            // Try to refresh
            var refreshed = await TryRefreshTokenAsync(tokens, cancellationToken);
            if (refreshed != null)
            {
                tokens = refreshed;
                await _tokenStore.SaveTokensAsync(TokenKey, tokens, cancellationToken);
            }
            else
            {
                return null;
            }
        }

        return await FetchAccountInfoAsync(tokens.AccessToken, cancellationToken);
    }

    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(AnthropicOAuthService));

        var tokens = await _tokenStore.LoadTokensAsync(TokenKey, cancellationToken);
        if (tokens == null || string.IsNullOrEmpty(tokens.AccessToken))
        {
            return null;
        }

        // Check if token needs refresh
        if (tokens.ExpiresAt.HasValue && tokens.ExpiresAt.Value <= DateTimeOffset.UtcNow + TokenExpiryBuffer)
        {
            var refreshed = await TryRefreshTokenAsync(tokens, cancellationToken);
            if (refreshed != null)
            {
                tokens = refreshed;
                await _tokenStore.SaveTokensAsync(TokenKey, tokens, cancellationToken);
            }
            else
            {
                return null;
            }
        }

        return tokens.AccessToken;
    }

    private static (string codeVerifier, string codeChallenge) GeneratePkce()
    {
        var codeVerifier = GenerateRandomString(64);
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(codeVerifier));
        var codeChallenge = Base64UrlEncode(hash);
        return (codeVerifier, codeChallenge);
    }

    private static string GenerateState()
    {
        return GenerateRandomString(32);
    }

    private static string GenerateRandomString(int length)
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~";
        var bytes = RandomNumberGenerator.GetBytes(length);
        var result = new char[length];
        for (var i = 0; i < length; i++)
        {
            result[i] = chars[bytes[i] % chars.Length];
        }
        return new string(result);
    }

    private static string Base64UrlEncode(byte[] data)
    {
        return Convert.ToBase64String(data)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static void OpenBrowser(string url)
    {
        // Cross-platform browser opening
        var isUnix = Environment.OSVersion.Platform == PlatformID.Unix ||
                     Environment.OSVersion.Platform == PlatformID.MacOSX;

        if (OperatingSystem.IsMacOS())
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "open",
                Arguments = url,
                UseShellExecute = false
            });
        }
        else if (OperatingSystem.IsLinux())
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "xdg-open",
                Arguments = url,
                UseShellExecute = false
            });
        }
        else if (OperatingSystem.IsWindows())
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        else
        {
            throw new PlatformNotSupportedException("Cannot open browser on this platform.");
        }
    }

    private async Task<string> WaitForCallbackAsync(int port, string expectedState, CancellationToken cancellationToken)
    {
        var listener = new HttpListener();
        listener.Prefixes.Add($"http://localhost:{port}/callback/");

        try
        {
            listener.Start();
        }
        catch (HttpListenerException ex)
        {
            throw new InvalidOperationException(
                $"Could not start callback server on port {port}. Port may be in use. Error: {ex.Message}", ex);
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromMinutes(5));

        try
        {
            var context = await listener.GetContextAsync().WaitAsync(cts.Token);

            var queryString = context.Request.Url?.Query ?? "";
            var queryParams = HttpUtility.ParseQueryString(queryString);

            var code = queryParams["code"];
            var state = queryParams["state"];
            var error = queryParams["error"];

            // Respond to browser
            var response = context.Response;
            response.ContentType = "text/html; charset=utf-8";

            if (!string.IsNullOrEmpty(error))
            {
                var errorDesc = queryParams["error_description"] ?? error;
                var errorHtml = $"<html><body><h2>Authentication failed</h2><p>{System.Net.WebUtility.HtmlEncode(errorDesc)}</p><p>You can close this window.</p></body></html>";
                var buffer = Encoding.UTF8.GetBytes(errorHtml);
                response.ContentLength64 = buffer.Length;
                await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                response.Close();
                throw new InvalidOperationException($"OAuth error: {errorDesc}");
            }

            if (string.IsNullOrEmpty(code))
            {
                var errorHtml = "<html><body><h2>Authentication failed</h2><p>No authorization code received.</p><p>You can close this window.</p></body></html>";
                var buffer = Encoding.UTF8.GetBytes(errorHtml);
                response.ContentLength64 = buffer.Length;
                await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                response.Close();
                throw new InvalidOperationException("No authorization code received.");
            }

            if (state != expectedState)
            {
                var errorHtml = "<html><body><h2>Authentication failed</h2><p>State mismatch. Possible CSRF.</p><p>You can close this window.</p></body></html>";
                var buffer = Encoding.UTF8.GetBytes(errorHtml);
                response.ContentLength64 = buffer.Length;
                await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                response.Close();
                throw new InvalidOperationException("State mismatch. Possible CSRF.");
            }

            var successHtml = "<html><body><h2>Authentication successful!</h2><p>You can close this window and return to ClawdNet.</p></body></html>";
            var successBuffer = Encoding.UTF8.GetBytes(successHtml);
            response.ContentLength64 = successBuffer.Length;
            await response.OutputStream.WriteAsync(successBuffer, 0, successBuffer.Length);
            response.Close();

            return code;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || cts.IsCancellationRequested)
        {
            throw new TimeoutException("OAuth login timed out. Please try again.");
        }
        finally
        {
            if (listener.IsListening)
            {
                listener.Stop();
            }
        }
    }

    private async Task<OAuthTokens> ExchangeCodeForTokensAsync(string code, string codeVerifier, int callbackPort, CancellationToken cancellationToken)
    {
        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "authorization_code"),
            new KeyValuePair<string, string>("client_id", ClientId),
            new KeyValuePair<string, string>("code", code),
            new KeyValuePair<string, string>("redirect_uri", $"http://localhost:{callbackPort}/callback"),
            new KeyValuePair<string, string>("code_verifier", codeVerifier),
        });

        var response = await _httpClient.PostAsync(TokenUrl, content, cancellationToken);
        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Token exchange failed: {responseJson}");
        }

        var tokenResponse = JsonSerializer.Deserialize<TokenResponse>(responseJson)
            ?? throw new InvalidOperationException("Failed to parse token response.");

        return new OAuthTokens
        {
            AccessToken = tokenResponse.AccessToken,
            RefreshToken = tokenResponse.RefreshToken ?? string.Empty,
            ExpiresAt = tokenResponse.ExpiresIn > 0
                ? DateTimeOffset.UtcNow.AddSeconds(tokenResponse.ExpiresIn)
                : null,
            Scopes = tokenResponse.Scope?.Split(' ').ToList() ?? []
        };
    }

    private async Task<OAuthTokens?> TryRefreshTokenAsync(OAuthTokens tokens, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(tokens.RefreshToken))
        {
            return null;
        }

        try
        {
            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "refresh_token"),
                new KeyValuePair<string, string>("client_id", ClientId),
                new KeyValuePair<string, string>("refresh_token", tokens.RefreshToken),
            });

            var response = await _httpClient.PostAsync(TokenUrl, content, cancellationToken);
            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var tokenResponse = JsonSerializer.Deserialize<TokenResponse>(responseJson);
            if (tokenResponse == null)
            {
                return null;
            }

            return new OAuthTokens
            {
                AccessToken = tokenResponse.AccessToken,
                RefreshToken = tokenResponse.RefreshToken ?? tokens.RefreshToken,
                ExpiresAt = tokenResponse.ExpiresIn > 0
                    ? DateTimeOffset.UtcNow.AddSeconds(tokenResponse.ExpiresIn)
                    : tokens.ExpiresAt,
                Scopes = tokenResponse.Scope?.Split(' ').ToList() ?? tokens.Scopes
            };
        }
        catch
        {
            return null;
        }
    }

    private async Task<OAuthAccountInfo> FetchAccountInfoAsync(string accessToken, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, ProfileUrl);
        request.Headers.Add("Authorization", $"Bearer {accessToken}");

        var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Failed to fetch account info: {responseJson}");
        }

        using var doc = JsonDocument.Parse(responseJson);
        var root = doc.RootElement;

        var email = root.TryGetProperty("email", out var emailProp) ? emailProp.GetString() ?? "" : "";
        var organization = root.TryGetProperty("organization", out var orgProp) ? orgProp.GetString() ?? "" : "";
        var subscriptionType = root.TryGetProperty("subscription_type", out var subProp) ? subProp.GetString() ?? "" : "";

        return new OAuthAccountInfo
        {
            Email = email,
            Organization = organization,
            SubscriptionType = subscriptionType,
            RateLimitTier = "" // Will be populated by FetchUserRolesAsync
        };
    }

    private async Task FetchUserRolesAsync(string accessToken, CancellationToken cancellationToken)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, RolesUrl);
            request.Headers.Add("Authorization", $"Bearer {accessToken}");

            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return;
            }

            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(responseJson);
            // Roles array — we just log them, don't need to store for basic auth status
        }
        catch
        {
            // Non-fatal
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _httpClient?.Dispose();
        await Task.CompletedTask;
    }

    private sealed record TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; init; } = string.Empty;

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; init; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; init; }

        [JsonPropertyName("scope")]
        public string? Scope { get; init; }

        [JsonPropertyName("token_type")]
        public string TokenType { get; init; } = "Bearer";
    }
}
