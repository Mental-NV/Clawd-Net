using System.Text.Json.Nodes;
using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Models;

namespace ClawdNet.Tests.TestDoubles;

public sealed class FakeMcpClient : IMcpClient
{
    public List<(string ServerName, string ToolName, JsonNode? Input)> Invocations { get; } = [];

    public IReadOnlyCollection<McpServerState> Servers { get; set; } = [];

    public IReadOnlyList<McpToolDefinition> Tools { get; set; } = [];

    public Func<string, McpServerState?> PingHandler { get; set; } = _ => null;

    public int ReloadCount { get; private set; }

    public Func<FakeMcpClient, Task>? ReloadHandler { get; set; }

    public Func<string, string, JsonNode?, ToolExecutionResult> InvokeHandler { get; set; }
        = (_, toolName, _) => new ToolExecutionResult(true, $"mcp:{toolName}", null);

    public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task ReloadAsync(CancellationToken cancellationToken)
    {
        ReloadCount++;
        if (ReloadHandler is not null)
        {
            await ReloadHandler(this);
        }
    }

    public Task<McpServerState?> PingAsync(string serverName, CancellationToken cancellationToken)
        => Task.FromResult(PingHandler(serverName));

    public Task<IReadOnlyList<McpToolDefinition>> GetToolsAsync(string? serverName, CancellationToken cancellationToken)
    {
        IReadOnlyList<McpToolDefinition> tools = string.IsNullOrWhiteSpace(serverName)
            ? Tools
            : Tools.Where(tool => string.Equals(tool.ServerName, serverName, StringComparison.OrdinalIgnoreCase)).ToArray();
        return Task.FromResult(tools);
    }

    public Task<ToolExecutionResult> InvokeToolAsync(string serverName, string toolName, JsonNode? input, CancellationToken cancellationToken)
    {
        Invocations.Add((serverName, toolName, input));
        return Task.FromResult(InvokeHandler(serverName, toolName, input));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
