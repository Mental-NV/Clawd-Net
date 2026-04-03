using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Models;

namespace ClawdNet.Core.Commands;

public sealed class McpCommandHandler : ICommandHandler
{
    public string Name => "mcp";

    public string HelpSummary => "List, ping, and inspect MCP servers";

    public string HelpText => """
Usage: clawdnet mcp list
       clawdnet mcp ping <server>
       clawdnet mcp tools [server]

Inspect configured MCP (Model Context Protocol) servers.

Commands:
  list              List all configured MCP servers
  ping <server>     Check if an MCP server is connected
  tools [server]    List tools available from MCP servers

Examples:
  clawdnet mcp list
  clawdnet mcp ping demo
  clawdnet mcp tools
  clawdnet mcp tools demo
""";

    public bool CanHandle(CommandRequest request)
    {
        return request.Arguments.Count > 0
            && string.Equals(request.Arguments[0], "mcp", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<CommandExecutionResult> ExecuteAsync(
        CommandContext context,
        CommandRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Arguments.Count < 2)
        {
            return CommandExecutionResult.Failure("mcp requires a subcommand: list, ping <server>, tools [server].");
        }

        return request.Arguments[1].ToLowerInvariant() switch
        {
            "list" => await ListAsync(context.McpClient, cancellationToken),
            "ping" => await PingAsync(context.McpClient, request.Arguments.Skip(2).ToArray(), cancellationToken),
            "tools" => await ToolsAsync(context.McpClient, request.Arguments.Skip(2).ToArray(), cancellationToken),
            _ => CommandExecutionResult.Failure($"Unknown mcp subcommand '{request.Arguments[1]}'.")
        };
    }

    private static async Task<CommandExecutionResult> ListAsync(IMcpClient mcpClient, CancellationToken cancellationToken)
    {
        await mcpClient.InitializeAsync(cancellationToken);
        if (mcpClient.Servers.Count == 0)
        {
            return CommandExecutionResult.Success("No MCP servers configured.");
        }

        var lines = mcpClient.Servers.Select(server =>
            $"{server.Name} | enabled={server.Enabled} | connected={server.Connected} | tools={server.ToolCount}" +
            (string.IsNullOrWhiteSpace(server.Error) ? string.Empty : $" | error={server.Error}"));
        return CommandExecutionResult.Success(string.Join(Environment.NewLine, lines));
    }

    private static async Task<CommandExecutionResult> PingAsync(IMcpClient mcpClient, string[] args, CancellationToken cancellationToken)
    {
        if (args.Length == 0 || string.IsNullOrWhiteSpace(args[0]))
        {
            return CommandExecutionResult.Failure("mcp ping requires a server name.");
        }

        var result = await mcpClient.PingAsync(args[0], cancellationToken);
        if (result is null)
        {
            return CommandExecutionResult.Failure($"MCP server '{args[0]}' was not found.", 3);
        }

        return result.Connected
            ? CommandExecutionResult.Success($"{result.Name} is connected ({result.ToolCount} tools).")
            : CommandExecutionResult.Failure(result.Error ?? $"MCP server '{result.Name}' is unavailable.", 2);
    }

    private static async Task<CommandExecutionResult> ToolsAsync(IMcpClient mcpClient, string[] args, CancellationToken cancellationToken)
    {
        var serverName = args.Length > 0 ? args[0] : null;
        var tools = await mcpClient.GetToolsAsync(serverName, cancellationToken);
        if (tools.Count == 0)
        {
            return CommandExecutionResult.Success(string.IsNullOrWhiteSpace(serverName)
                ? "No MCP tools available."
                : $"No MCP tools available for server '{serverName}'.");
        }

        var lines = tools.Select(tool =>
            $"mcp.{tool.ServerName}.{tool.Name} | server={tool.ServerName} | readOnly={tool.ReadOnly} | {tool.Description}".TrimEnd());
        return CommandExecutionResult.Success(string.Join(Environment.NewLine, lines));
    }
}
