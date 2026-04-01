namespace ClawdNet.Core.Models;

public sealed record LspServerState(
    string Name,
    bool Enabled,
    bool Connected,
    IReadOnlyList<string> FileExtensions,
    string? Error = null);
