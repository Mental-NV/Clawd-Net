namespace ClawdNet.Core.Models;

public sealed record ToolExecutionResult(bool Success, string Output, string? Error = null);
