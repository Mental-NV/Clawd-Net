namespace ClawdNet.Core.Models;

public sealed record PtySessionSummary(
    string SessionId,
    string Command,
    string WorkingDirectory,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    bool IsRunning,
    int? ExitCode,
    bool IsCurrent,
    bool IsOutputClipped);
