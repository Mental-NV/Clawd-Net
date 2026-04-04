namespace ClawdNet.Core.Models;

/// <summary>
/// Transport types supported by MCP servers.
/// </summary>
public enum McpTransportType
{
    /// <summary>
    /// Standard I/O transport (process stdin/stdout).
    /// </summary>
    Stdio,

    /// <summary>
    /// Server-Sent Events transport.
    /// </summary>
    Sse,

    /// <summary>
    /// HTTP streaming transport.
    /// </summary>
    Http,
}

public sealed record McpServerDefinition(
    string Name,
    string Command,
    IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string> Environment,
    bool Enabled = true,
    bool ToolsReadOnly = false,
    McpTransportType Transport = McpTransportType.Stdio,
    string? Url = null,
    IReadOnlyDictionary<string, string>? Headers = null);
