using System.Text.Json.Nodes;
using ClawdNet.Core.Models;

namespace ClawdNet.Core.Abstractions;

public interface IMcpClient : IAsyncDisposable
{
    IReadOnlyCollection<McpServerState> Servers { get; }

    Task InitializeAsync(CancellationToken cancellationToken);

    Task ReloadAsync(CancellationToken cancellationToken);

    Task<McpServerState?> PingAsync(string serverName, CancellationToken cancellationToken);

    Task<IReadOnlyList<McpToolDefinition>> GetToolsAsync(string? serverName, CancellationToken cancellationToken);

    Task<ToolExecutionResult> InvokeToolAsync(string serverName, string toolName, JsonNode? input, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the raw server definitions from the configuration (for management commands).
    /// </summary>
    Task<IReadOnlyList<McpServerDefinition>> GetServerDefinitionsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Adds or updates an MCP server definition in the configuration.
    /// </summary>
    Task AddServerAsync(McpServerDefinition server, CancellationToken cancellationToken);

    /// <summary>
    /// Removes an MCP server definition from the configuration.
    /// </summary>
    Task<bool> RemoveServerAsync(string name, CancellationToken cancellationToken);
}
