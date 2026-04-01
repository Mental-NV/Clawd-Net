namespace ClawdNet.Core.Models;

public sealed record PtySessionState(
    string SessionId,
    string Command,
    string WorkingDirectory,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    bool IsRunning,
    int? ExitCode,
    string RecentOutput,
    bool IsOutputClipped);
