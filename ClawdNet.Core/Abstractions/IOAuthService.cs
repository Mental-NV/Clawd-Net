using ClawdNet.Core.Models;

namespace ClawdNet.Core.Abstractions;

/// <summary>
/// OAuth authentication service for interactive browser-based login.
/// </summary>
public interface IOAuthService : IAsyncDisposable
{
    /// <summary>
    /// Whether OAuth login is supported (tokens can be stored and refreshed).
    /// </summary>
    bool IsSupported { get; }

    /// <summary>
    /// Initiate the OAuth login flow. Opens a browser and waits for callback.
    /// Returns the account profile on success.
    /// </summary>
    Task<OAuthAccountInfo> LoginAsync(OAuthLoginOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Log out by clearing stored OAuth tokens.
    /// </summary>
    Task LogoutAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get current account info if tokens are present and valid.
    /// </summary>
    Task<OAuthAccountInfo?> GetAccountInfoAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a valid access token, refreshing if necessary.
    /// Returns null if no tokens are stored.
    /// </summary>
    Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Options for OAuth login flow.
/// </summary>
public sealed record OAuthLoginOptions
{
    /// <summary>
    /// Port for the local callback server. Default: 9876.
    /// </summary>
    public int CallbackPort { get; init; } = 9876;
}

/// <summary>
/// OAuth account profile information.
/// </summary>
public sealed record OAuthAccountInfo
{
    public string Email { get; init; } = string.Empty;
    public string Organization { get; init; } = string.Empty;
    public string SubscriptionType { get; init; } = string.Empty;
    public string RateLimitTier { get; init; } = string.Empty;
}
