using ClawdNet.Core.Abstractions;

namespace ClawdNet.Core.Models;

public sealed record QueryRequest(
    string Prompt,
    string? SessionId = null,
    string? Model = null,
    int MaxTurns = 8,
    PermissionMode PermissionMode = PermissionMode.Default,
    IToolApprovalHandler? ApprovalHandler = null,
    bool AllowTaskTools = true,
    string? Provider = null);
