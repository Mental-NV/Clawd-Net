namespace ClawdNet.Core.Models;

public sealed record TaskResult(
    bool Success,
    string Summary,
    string? Error = null);
