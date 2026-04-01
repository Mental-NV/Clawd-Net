namespace ClawdNet.Core.Models;

public sealed record PtyOutputChunk(
    string Text,
    bool IsError,
    DateTimeOffset TimestampUtc);
