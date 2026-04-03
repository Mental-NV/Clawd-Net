namespace ClawdNet.Core.Models;

public sealed record PtyManagerState(
    string? CurrentSessionId,
    PtySessionState? CurrentSession,
    IReadOnlyList<PtySessionSummary> Sessions);
