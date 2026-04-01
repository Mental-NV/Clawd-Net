using System.Text.Json.Nodes;
using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Models;

namespace ClawdNet.Runtime.Tools;

public sealed class McpToolAdapter : ITool
{
    private readonly IMcpClient _mcpClient;
    private readonly string _serverName;
    private readonly string _toolName;

    public McpToolAdapter(IMcpClient mcpClient, McpToolDefinition definition)
    {
        _mcpClient = mcpClient;
        _serverName = definition.ServerName;
        _toolName = definition.Name;
        Name = $"mcp.{definition.ServerName}.{definition.Name}";
        Description = string.IsNullOrWhiteSpace(definition.Description)
            ? $"MCP tool '{definition.Name}' from server '{definition.ServerName}'."
            : $"[{definition.ServerName}] {definition.Description}";
        InputSchema = definition.InputSchema;
        Category = definition.ReadOnly ? ToolCategory.ReadOnly : ToolCategory.Execute;
    }

    public string Name { get; }

    public string Description { get; }

    public JsonObject InputSchema { get; }

    public ToolCategory Category { get; }

    public Task<ToolExecutionResult> ExecuteAsync(ToolExecutionRequest request, CancellationToken cancellationToken)
        => _mcpClient.InvokeToolAsync(_serverName, _toolName, request.Input, cancellationToken);
}
