namespace ClawdNet.Core.Models;

public sealed record ToolResult(
    string ToolCallId,
    string ToolName,
    string Output,
    bool IsError);
