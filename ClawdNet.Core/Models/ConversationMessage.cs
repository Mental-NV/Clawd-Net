namespace ClawdNet.Core.Models;

public sealed record ConversationMessage(
    string Role,
    string Content,
    DateTimeOffset TimestampUtc,
    string? ToolName = null,
    string? ToolCallId = null,
    bool IsError = false);
