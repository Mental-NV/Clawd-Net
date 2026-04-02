namespace ClawdNet.Core.Models;

public sealed record TaskWorkerSnapshot(
    string WorkerSessionId,
    int MessageCount,
    DateTimeOffset? UpdatedAtUtc,
    string TranscriptTail);
