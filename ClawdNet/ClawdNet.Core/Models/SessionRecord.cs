namespace ClawdNet.Core.Models;

public sealed record SessionRecord(
    string Id,
    string Title,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyList<TranscriptEntry> Transcript);
