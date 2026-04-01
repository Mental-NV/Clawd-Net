namespace ClawdNet.Core.Models;

public sealed record TranscriptEntry(
    string Role,
    string Content,
    DateTimeOffset TimestampUtc);
