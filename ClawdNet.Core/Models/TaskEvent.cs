namespace ClawdNet.Core.Models;

public sealed record TaskEvent(
    TaskStatus Status,
    string Message,
    DateTimeOffset TimestampUtc,
    bool IsError = false);
