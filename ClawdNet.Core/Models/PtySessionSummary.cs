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
    bool IsOutputClipped,
    TimeSpan? Timeout = null,
    bool IsBackground = false,
    DateTimeOffset? CompletedAtUtc = null,
    int OutputLineCount = 0)
{
    /// <summary>
    /// Computed duration from start to end (or now if still running).
    /// </summary>
    public TimeSpan Duration
    {
        get
        {
            var end = CompletedAtUtc ?? UpdatedAtUtc;
            return end - StartedAtUtc;
        }
    }
}
