namespace ClawdNet.Core.Models;

public sealed record McpServerState(
    string Name,
    bool Enabled,
    bool Connected,
    int ToolCount,
    string? Error = null);
