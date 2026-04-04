using System.Text.Json.Serialization;

namespace ClawdNet.Core.Models;

/// <summary>
/// OAuth token set with expiration metadata.
/// </summary>
public sealed record OAuthTokens
{
    [JsonPropertyName("accessToken")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("refreshToken")]
    public string RefreshToken { get; set; } = string.Empty;

    [JsonPropertyName("expiresAt")]
    public DateTimeOffset? ExpiresAt { get; set; }

    [JsonPropertyName("scopes")]
    public List<string> Scopes { get; set; } = [];
}
