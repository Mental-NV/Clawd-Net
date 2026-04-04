using System.Text.Json.Nodes;
using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Models;

namespace ClawdNet.Runtime.Protocols;

public sealed class NullMcpClient : IMcpClient
{
    public IReadOnlyCollection<McpServerState> Servers => [];

    public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task ReloadAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<McpServerState?> PingAsync(string serverName, CancellationToken cancellationToken)
        => Task.FromResult<McpServerState?>(null);

    public Task<IReadOnlyList<McpToolDefinition>> GetToolsAsync(string? serverName, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<McpToolDefinition>>([]);

    public Task<ToolExecutionResult> InvokeToolAsync(string serverName, string toolName, JsonNode? input, CancellationToken cancellationToken)
        => Task.FromResult(new ToolExecutionResult(false, string.Empty, $"MCP server '{serverName}' is not available."));

    public Task<IReadOnlyList<McpServerDefinition>> GetServerDefinitionsAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<McpServerDefinition>>([]);

    public Task AddServerAsync(McpServerDefinition server, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<bool> RemoveServerAsync(string name, CancellationToken cancellationToken) => Task.FromResult(false);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
