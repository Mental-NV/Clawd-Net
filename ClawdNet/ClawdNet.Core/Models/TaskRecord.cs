namespace ClawdNet.Core.Models;

public sealed record TaskRecord(
    string Id,
    TaskKind Kind,
    string Title,
    string Goal,
    string ParentSessionId,
    string WorkerSessionId,
    string Model,
    PermissionMode PermissionMode,
    TaskStatus Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? CompletedAtUtc = null,
    string? ParentSummary = null,
    string? WorkingDirectory = null,
    string? LastStatusMessage = null,
    TaskResult? Result = null,
    IReadOnlyList<TaskEvent>? Events = null,
    string? WorkerTranscriptTail = null,
    int WorkerMessageCount = 0,
    DateTimeOffset? WorkerUpdatedAtUtc = null,
    string? InterruptionReason = null);
