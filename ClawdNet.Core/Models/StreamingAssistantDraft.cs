namespace ClawdNet.Core.Models;

public sealed record StreamingAssistantDraft(
    string Text,
    bool IsActive,
    string? ToolName = null,
    string? Detail = null);
