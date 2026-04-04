namespace ClawdNet.Core.Models;

public sealed record ConversationSession(
    string Id,
    string Title,
    string Model,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<ConversationMessage> Messages,
    string? Provider = null,
    IReadOnlyList<string>? Tags = null)
{
    /// <summary>
    /// Tags associated with the session. Defaults to an empty list for backward compatibility.
    /// </summary>
    public IReadOnlyList<string> EffectiveTags => Tags ?? [];
}
