namespace ClawdNet.Core.Models;

public sealed record PlatformLaunchResult(
    bool Success,
    string Message,
    string? Error = null);
