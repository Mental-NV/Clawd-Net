using ClawdNet.Core.Models;

namespace ClawdNet.Core.Abstractions;

/// <summary>
/// Secure token storage for OAuth credentials.
/// </summary>
public interface ITokenStore : IAsyncDisposable
{
    Task SaveTokensAsync(string key, OAuthTokens tokens, CancellationToken cancellationToken = default);
    Task<OAuthTokens?> LoadTokensAsync(string key, CancellationToken cancellationToken = default);
    Task<bool> DeleteTokensAsync(string key, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> ListKeysAsync(CancellationToken cancellationToken = default);
}
