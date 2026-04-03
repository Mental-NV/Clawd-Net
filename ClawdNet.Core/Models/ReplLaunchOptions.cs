namespace ClawdNet.Core.Models;

public sealed record ReplLaunchOptions(
    string? SessionId = null,
    string? Model = null,
    PermissionMode PermissionMode = PermissionMode.Default,
    string? Provider = null,
    string? InitialPrompt = null,
    bool Continue = false,
    string? ResumeQuery = null);
