namespace ClawdNet.Core.Models;

public sealed record PlatformOpenRequest(
    string Path,
    int? Line = null,
    int? Column = null,
    bool Reveal = false,
    string? WorkingDirectory = null);
