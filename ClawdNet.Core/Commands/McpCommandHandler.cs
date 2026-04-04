using System.Text.Json;
using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Models;

namespace ClawdNet.Core.Commands;

public sealed class McpCommandHandler : ICommandHandler
{
    public string Name => "mcp";

    public string HelpSummary => "List, ping, inspect, add, remove, and manage MCP servers";

    public string HelpText => """
Usage: clawdnet mcp list
       clawdnet mcp ping <server>
       clawdnet mcp tools [server]
       clawdnet mcp get <server>
       clawdnet mcp add <name> <command> [args...]
       clawdnet mcp remove <name>
       clawdnet mcp add-json <name> <json>

Manage MCP (Model Context Protocol) servers.

Commands:
  list                List all configured MCP servers
  ping <server>       Check if an MCP server is connected
  tools [server]      List tools available from MCP servers
  get <server>        Show detailed info about a specific MCP server
  add <name> <cmd>    Add an MCP server (stdio transport)
  remove <name>       Remove an MCP server
  add-json <n> <json> Add an MCP server from a JSON string

Add flags:
  -e, --env KEY=VALUE    Environment variable (repeatable)
  --read-only-tools      Mark server tools as read-only

Examples:
  clawdnet mcp list
  clawdnet mcp ping demo
  clawdnet mcp tools
  clawdnet mcp get demo
  clawdnet mcp add demo python3 /path/to/server.py -e DEMO_FLAG=1
  clawdnet mcp remove demo
  clawdnet mcp add-json test '{"command":"echo","arguments":["hello"]}'
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
            return CommandExecutionResult.Failure("mcp requires a subcommand: list, ping <server>, tools [server], get <server>, add <name> <cmd> [args...], remove <name>, add-json <name> <json>.");
        }

        return request.Arguments[1].ToLowerInvariant() switch
        {
            "list" => await ListAsync(context.McpClient, cancellationToken),
            "ping" => await PingAsync(context.McpClient, request.Arguments.Skip(2).ToArray(), cancellationToken),
            "tools" => await ToolsAsync(context.McpClient, request.Arguments.Skip(2).ToArray(), cancellationToken),
            "get" => await GetAsync(context.McpClient, request.Arguments.Skip(2).ToArray(), cancellationToken),
            "add" => await AddAsync(context.McpClient, request.Arguments.Skip(2).ToArray(), cancellationToken),
            "remove" => await RemoveAsync(context.McpClient, request.Arguments.Skip(2).ToArray(), cancellationToken),
            "add-json" => await AddJsonAsync(context.McpClient, request.Arguments.Skip(2).ToArray(), cancellationToken),
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

    private static async Task<CommandExecutionResult> GetAsync(IMcpClient mcpClient, string[] args, CancellationToken cancellationToken)
    {
        if (args.Length == 0 || string.IsNullOrWhiteSpace(args[0]))
        {
            return CommandExecutionResult.Failure("mcp get requires a server name.");
        }

        var serverName = args[0];
        var servers = await mcpClient.GetServerDefinitionsAsync(cancellationToken);
        var server = servers.FirstOrDefault(s => string.Equals(s.Name, serverName, StringComparison.OrdinalIgnoreCase));
        if (server is null)
        {
            return CommandExecutionResult.Failure($"MCP server '{serverName}' not found.", 3);
        }

        // Try to get runtime state
        var state = mcpClient.Servers.FirstOrDefault(s => string.Equals(s.Name, serverName, StringComparison.OrdinalIgnoreCase));

        var lines = new List<string>
        {
            $"Name:          {server.Name}",
            $"Transport:     {server.Transport}",
            $"Command:       {server.Command}",
            $"Arguments:     {string.Join(" ", server.Arguments)}",
            $"Enabled:       {server.Enabled}",
            $"Read-only:     {server.ToolsReadOnly}",
        };

        if (server.Environment.Count > 0)
        {
            lines.Add("Environment:");
            foreach (var kvp in server.Environment)
            {
                lines.Add($"  {kvp.Key}={MaskSensitiveValue(kvp.Key, kvp.Value)}");
            }
        }

        if (!string.IsNullOrWhiteSpace(server.Url))
        {
            lines.Add($"URL:           {server.Url}");
        }

        if (server.Headers is { Count: > 0 })
        {
            lines.Add("Headers:");
            foreach (var kvp in server.Headers)
            {
                lines.Add($"  {kvp.Key}: {MaskSensitiveValue(kvp.Key, kvp.Value)}");
            }
        }

        if (state is not null)
        {
            lines.Add(string.Empty);
            lines.Add($"Runtime state:");
            lines.Add($"  Connected:   {state.Connected}");
            lines.Add($"  Tools:       {state.ToolCount}");
            if (!string.IsNullOrWhiteSpace(state.Error))
            {
                lines.Add($"  Error:       {state.Error}");
            }
        }

        return CommandExecutionResult.Success(string.Join(Environment.NewLine, lines));
    }

    private static async Task<CommandExecutionResult> AddAsync(IMcpClient mcpClient, string[] args, CancellationToken cancellationToken)
    {
        if (args.Length < 2)
        {
            return CommandExecutionResult.Failure("mcp add requires a name and command: mcp add <name> <command> [args...] [-e KEY=VALUE] [--read-only-tools].");
        }

        var name = args[0];
        var command = args[1];
        var commandArgs = new List<string>();
        var envVars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var readOnlyTools = false;

        for (var i = 2; i < args.Length; i++)
        {
            if (args[i] is "-e" or "--env" && i + 1 < args.Length)
            {
                var parts = args[++i].Split('=', 2);
                if (parts.Length == 2)
                {
                    envVars[parts[0]] = parts[1];
                }
            }
            else if (args[i] == "--read-only-tools")
            {
                readOnlyTools = true;
            }
            else
            {
                commandArgs.Add(args[i]);
            }
        }

        var server = new McpServerDefinition(
            name, command, commandArgs, envVars, Enabled: true, ToolsReadOnly: readOnlyTools);
        await mcpClient.AddServerAsync(server, cancellationToken);
        return CommandExecutionResult.Success($"MCP server '{name}' added.");
    }

    private static async Task<CommandExecutionResult> RemoveAsync(IMcpClient mcpClient, string[] args, CancellationToken cancellationToken)
    {
        if (args.Length == 0 || string.IsNullOrWhiteSpace(args[0]))
        {
            return CommandExecutionResult.Failure("mcp remove requires a server name.");
        }

        var name = args[0];
        var removed = await mcpClient.RemoveServerAsync(name, cancellationToken);
        if (!removed)
        {
            return CommandExecutionResult.Failure($"MCP server '{name}' not found.", 3);
        }

        return CommandExecutionResult.Success($"MCP server '{name}' removed.");
    }

    private static async Task<CommandExecutionResult> AddJsonAsync(IMcpClient mcpClient, string[] args, CancellationToken cancellationToken)
    {
        if (args.Length < 2)
        {
            return CommandExecutionResult.Failure("mcp add-json requires a name and JSON string: mcp add-json <name> '<json>'.");
        }

        var name = args[0];
        var json = string.Join(" ", args.Skip(1));

        McpServerDefinition? server;
        try
        {
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var command = root.GetProperty("command").GetString() ?? string.Empty;
            var arguments = root.TryGetProperty("arguments", out var argsElem)
                ? argsElem.EnumerateArray().Select(e => e.GetString() ?? string.Empty).ToArray()
                : Array.Empty<string>();
            var environment = root.TryGetProperty("environment", out var envElem)
                ? envElem.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var enabled = root.TryGetProperty("enabled", out var enabledElem) && enabledElem.GetBoolean();
            var readOnlyTools = root.TryGetProperty("toolsReadOnly", out var roElem) && roElem.GetBoolean();
            var transport = root.TryGetProperty("transport", out var tElem)
                ? Enum.TryParse<McpTransportType>(tElem.GetString(), ignoreCase: true, out var parsed) ? parsed : McpTransportType.Stdio
                : McpTransportType.Stdio;
            var url = root.TryGetProperty("url", out var urlElem) ? urlElem.GetString() : null;
            var headers = root.TryGetProperty("headers", out var hElem)
                ? hElem.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                : null;

            server = new McpServerDefinition(name, command, arguments, environment, enabled || !root.TryGetProperty("enabled", out _), readOnlyTools, transport, url, headers);
        }
        catch (JsonException ex)
        {
            return CommandExecutionResult.Failure($"Invalid JSON: {ex.Message}");
        }
        catch (InvalidOperationException ex)
        {
            return CommandExecutionResult.Failure($"Invalid JSON structure: {ex.Message}");
        }

        await mcpClient.AddServerAsync(server, cancellationToken);
        return CommandExecutionResult.Success($"MCP server '{name}' added from JSON.");
    }

    private static string MaskSensitiveValue(string key, string value)
    {
        // Mask common sensitive env var values
        var lowerKey = key.ToLowerInvariant();
        if (lowerKey.Contains("key") || lowerKey.Contains("token") || lowerKey.Contains("secret") || lowerKey.Contains("password"))
        {
            return value.Length > 4 ? $"{value[..2]}***{value[^2..]}" : "***";
        }
        return value;
    }
}
