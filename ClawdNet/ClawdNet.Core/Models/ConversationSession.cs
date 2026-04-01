namespace ClawdNet.Core.Models;

public sealed record ConversationSession(
    string Id,
    string Title,
    string Model,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<ConversationMessage> Messages);
