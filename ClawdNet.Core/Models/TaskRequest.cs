namespace ClawdNet.Core.Models;

public sealed record TaskRequest(
    string Title,
    string Goal,
    string ParentSessionId,
    string? ParentTaskId = null,
    string? ParentSummary = null,
    string? WorkingDirectory = null,
    string? Model = null,
    PermissionMode PermissionMode = PermissionMode.Default,
    int MaxTurns = 8,
    string? Provider = null);
