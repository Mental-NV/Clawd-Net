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
}
