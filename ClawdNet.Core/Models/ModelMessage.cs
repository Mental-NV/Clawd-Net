namespace ClawdNet.Core.Models;

public sealed record ModelMessage(
    string Role,
    string Content,
    string? ToolCallId = null,
    string? ToolName = null,
    bool IsError = false);
