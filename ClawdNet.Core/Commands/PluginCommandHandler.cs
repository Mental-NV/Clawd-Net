using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Models;
using ClawdNet.Core.Tools;

namespace ClawdNet.Core.Commands;

public sealed class PluginCommandHandler : ICommandHandler
{
    public string Name => "plugin";

    public string HelpSummary => "Manage local plugins";

    public string HelpText => """
Usage: clawdnet plugin list
       clawdnet plugin show <name>
       clawdnet plugin reload
       clawdnet plugin install <path>
       clawdnet plugin uninstall <name>
       clawdnet plugin enable <name>
       clawdnet plugin disable <name>
       clawdnet plugin status <name>

Manage locally installed plugins.

Commands:
  list                List all discovered plugins with health indicators
  show <name>         Show plugin details (tools, commands, hooks, errors)
  reload              Reload all plugins, MCP, and LSP servers
  install <path>      Install a plugin from a local path
  uninstall <name>    Uninstall a plugin by name
  enable <name>       Enable a plugin by name
  disable <name>      Disable a plugin by name
  status <name>       Show detailed plugin status including invocation metrics

Examples:
  clawdnet plugin list
  clawdnet plugin show demo
  clawdnet plugin reload
""";

    public bool CanHandle(CommandRequest request)
    {
        return request.Arguments.Count > 0
            && string.Equals(request.Arguments[0], "plugin", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<CommandExecutionResult> ExecuteAsync(
        CommandContext context,
        CommandRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Arguments.Count < 2)
        {
            return CommandExecutionResult.Failure("plugin requires a subcommand: list, reload.");
        }

        return request.Arguments[1].ToLowerInvariant() switch
        {
            "list" => await ListAsync(context, cancellationToken),
            "show" => await ShowAsync(context.PluginCatalog, request, cancellationToken),
            "reload" => await ReloadAsync(context, cancellationToken),
            "install" => await InstallAsync(context, request, cancellationToken),
            "uninstall" => await UninstallAsync(context, request, cancellationToken),
            "enable" => await EnableAsync(context, request, cancellationToken),
            "disable" => await DisableAsync(context, request, cancellationToken),
            "status" => await StatusAsync(context, request, cancellationToken),
            _ => CommandExecutionResult.Failure($"Unknown plugin subcommand '{request.Arguments[1]}'.")
        };
    }

    private static async Task<CommandExecutionResult> ListAsync(CommandContext context, CancellationToken cancellationToken)
    {
        await context.PluginCatalog.ReloadAsync(cancellationToken);
        if (context.PluginCatalog.Plugins.Count == 0)
        {
            return CommandExecutionResult.Success("No plugins discovered.");
        }

        var lines = context.PluginCatalog.Plugins.Select(plugin =>
        {
            var health = context.PluginRuntime.GetHealthMetrics(plugin.Name);
            var healthIndicator = health.HealthStatus switch
            {
                "healthy" => "✓",
                "idle" => "-",
                "degraded" => "⚠",
                "errors" => "✗",
                _ => "?"
            };
            var errors = plugin.Errors.Count == 0
                ? string.Empty
                : $" | errors={string.Join("; ", plugin.Errors.Select(error => $"{error.Code}:{error.Message}"))}";
            var totalInvocations = health.ToolInvocationCount + health.CommandInvocationCount + health.HookInvocationCount;
            return $"{healthIndicator} {plugin.Name} | enabled={plugin.Enabled} | valid={plugin.IsValid} | mcp={plugin.McpServers.Count} | lsp={plugin.LspServers.Count} | tools={plugin.Tools.Count} | commands={plugin.Commands.Count} | hooks={plugin.Hooks.Count} | invocations={totalInvocations}{errors}";
        });
        return CommandExecutionResult.Success(string.Join(Environment.NewLine, lines));
    }

    private static async Task<CommandExecutionResult> ShowAsync(
        IPluginCatalog pluginCatalog,
        CommandRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Arguments.Count < 3)
        {
            return CommandExecutionResult.Failure("plugin show requires a plugin name.");
        }

        await pluginCatalog.ReloadAsync(cancellationToken);
        var plugin = pluginCatalog.Plugins.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, request.Arguments[2], StringComparison.OrdinalIgnoreCase) ||
            string.Equals(candidate.Id, request.Arguments[2], StringComparison.OrdinalIgnoreCase));
        if (plugin is null)
        {
            return CommandExecutionResult.Failure($"Plugin '{request.Arguments[2]}' was not found.", 3);
        }

        var commandLines = plugin.Commands.Count == 0
            ? ["(none)"]
            : plugin.Commands.Select(command => $"{command.Name} => {command.Command} {string.Join(' ', command.Arguments)}".TrimEnd());
        var toolLines = plugin.Tools.Count == 0
            ? ["(none)"]
            : plugin.Tools.Select(tool => $"plugin.{plugin.Name}.{tool.Name} => {tool.Command} {string.Join(' ', tool.Arguments)} | category={tool.Category}".TrimEnd());
        var hookLines = plugin.Hooks.Count == 0
            ? ["(none)"]
            : plugin.Hooks.Select(hook => $"{hook.Kind} => {hook.Command} {string.Join(' ', hook.Arguments)} | blocking={hook.Blocking}".TrimEnd());
        var errors = plugin.Errors.Count == 0
            ? "(none)"
            : string.Join(Environment.NewLine, plugin.Errors.Select(error => $"{error.Code}: {error.Message}"));
        var output = string.Join(
            Environment.NewLine,
            [
                $"Plugin: {plugin.Name}",
                $"Id: {plugin.Id}",
                $"Enabled: {plugin.Enabled}",
                $"Valid: {plugin.IsValid}",
                "Tools:",
                .. toolLines,
                "Commands:",
                .. commandLines,
                "Hooks:",
                .. hookLines,
                "Errors:",
                errors
            ]);
        return CommandExecutionResult.Success(output);
    }

    private static async Task<CommandExecutionResult> ReloadAsync(CommandContext context, CancellationToken cancellationToken)
    {
        await context.PluginCatalog.ReloadAsync(cancellationToken);
        await context.PluginRuntime.ReloadAsync(cancellationToken);
        await context.McpClient.ReloadAsync(cancellationToken);
        await context.LspClient.ReloadAsync(cancellationToken);
        context.ToolRegistry.UnregisterWhere(tool => tool.Name.StartsWith("plugin.", StringComparison.OrdinalIgnoreCase));
        context.ToolRegistry.UnregisterWhere(tool => tool.Name.StartsWith("mcp.", StringComparison.OrdinalIgnoreCase));
        context.ToolRegistry.RegisterRange(
            context.PluginCatalog.Plugins
                .Where(plugin => plugin.Enabled && plugin.IsValid)
                .SelectMany(plugin => plugin.Tools.Where(tool => tool.Enabled).Select(tool => new PluginToolProxy(context.PluginRuntime, plugin, tool))));
        var tools = await context.McpClient.GetToolsAsync(null, cancellationToken);
        context.ToolRegistry.RegisterRange(tools.Select(tool => new McpToolProxy(context.McpClient, tool)));

        var output = string.Join(
            Environment.NewLine,
            [
                $"Reloaded {context.PluginCatalog.Plugins.Count} plugin(s).",
                $"MCP servers: {context.McpClient.Servers.Count}",
                $"LSP servers: {context.LspClient.Servers.Count}",
                $"Plugin tools: {context.PluginCatalog.Plugins.Sum(plugin => plugin.Tools.Count(tool => tool.Enabled))}",
                $"Plugin commands: {context.PluginCatalog.Plugins.Sum(plugin => plugin.Commands.Count(command => command.Enabled))}",
                $"Plugin hooks: {context.PluginCatalog.Plugins.Sum(plugin => plugin.Hooks.Count(hook => hook.Enabled))}"
            ]);
        return CommandExecutionResult.Success(output);
    }

    private static async Task<CommandExecutionResult> InstallAsync(CommandContext context, CommandRequest request, CancellationToken cancellationToken)
    {
        if (request.Arguments.Count < 3)
        {
            return CommandExecutionResult.Failure("plugin install requires a source path: plugin install <path>.");
        }

        var sourcePath = request.Arguments[2];
        if (!Directory.Exists(sourcePath))
        {
            return CommandExecutionResult.Failure($"Source path '{sourcePath}' does not exist.", 3);
        }

        try
        {
            var plugin = await context.PluginCatalog.InstallAsync(sourcePath, cancellationToken);
            return CommandExecutionResult.Success($"Plugin '{plugin.Name}' installed successfully.");
        }
        catch (Exception ex)
        {
            return CommandExecutionResult.Failure($"Failed to install plugin: {ex.Message}");
        }
    }

    private static async Task<CommandExecutionResult> UninstallAsync(CommandContext context, CommandRequest request, CancellationToken cancellationToken)
    {
        if (request.Arguments.Count < 3)
        {
            return CommandExecutionResult.Failure("plugin uninstall requires a plugin name: plugin uninstall <name>.");
        }

        var pluginName = request.Arguments[2];
        try
        {
            await context.PluginCatalog.UninstallAsync(pluginName, cancellationToken);
            return CommandExecutionResult.Success($"Plugin '{pluginName}' uninstalled successfully.");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
        {
            return CommandExecutionResult.Failure($"Plugin '{pluginName}' was not found.", 3);
        }
        catch (Exception ex)
        {
            return CommandExecutionResult.Failure($"Failed to uninstall plugin: {ex.Message}");
        }
    }

    private static async Task<CommandExecutionResult> EnableAsync(CommandContext context, CommandRequest request, CancellationToken cancellationToken)
    {
        if (request.Arguments.Count < 3)
        {
            return CommandExecutionResult.Failure("plugin enable requires a plugin name: plugin enable <name>.");
        }

        var pluginName = request.Arguments[2];
        try
        {
            var plugin = await context.PluginCatalog.EnableAsync(pluginName, cancellationToken);
            return CommandExecutionResult.Success($"Plugin '{plugin.Name}' enabled.");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
        {
            return CommandExecutionResult.Failure($"Plugin '{pluginName}' was not found.", 3);
        }
        catch (Exception ex)
        {
            return CommandExecutionResult.Failure($"Failed to enable plugin: {ex.Message}");
        }
    }

    private static async Task<CommandExecutionResult> DisableAsync(CommandContext context, CommandRequest request, CancellationToken cancellationToken)
    {
        if (request.Arguments.Count < 3)
        {
            return CommandExecutionResult.Failure("plugin disable requires a plugin name: plugin disable <name>.");
        }

        var pluginName = request.Arguments[2];
        try
        {
            var plugin = await context.PluginCatalog.DisableAsync(pluginName, cancellationToken);
            return CommandExecutionResult.Success($"Plugin '{plugin.Name}' disabled.");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
        {
            return CommandExecutionResult.Failure($"Plugin '{pluginName}' was not found.", 3);
        }
        catch (Exception ex)
        {
            return CommandExecutionResult.Failure($"Failed to disable plugin: {ex.Message}");
        }
    }

    private static async Task<CommandExecutionResult> StatusAsync(CommandContext context, CommandRequest request, CancellationToken cancellationToken)
    {
        if (request.Arguments.Count < 3)
        {
            return CommandExecutionResult.Failure("plugin status requires a plugin name: plugin status <name>.");
        }

        var pluginName = request.Arguments[2];
        await context.PluginCatalog.ReloadAsync(cancellationToken);

        var plugin = context.PluginCatalog.Plugins.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, pluginName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(candidate.Id, pluginName, StringComparison.OrdinalIgnoreCase));
        if (plugin is null)
        {
            return CommandExecutionResult.Failure($"Plugin '{pluginName}' was not found.", 3);
        }

        var health = context.PluginRuntime.GetHealthMetrics(pluginName);
        var hookDetails = plugin.Hooks.Select(hook =>
        {
            var marker = hook.Enabled ? "+" : "-";
            return $"  {marker} {hook.Kind} => {hook.Command} | blocking={hook.Blocking}";
        }).ToArray();

        var toolDetails = plugin.Tools.Select(tool =>
        {
            var marker = tool.Enabled ? "+" : "-";
            return $"  {marker} plugin.{plugin.Name}.{tool.Name} => {tool.Command} | category={tool.Category}";
        }).ToArray();

        var commandDetails = plugin.Commands.Select(command =>
        {
            var marker = command.Enabled ? "+" : "-";
            return $"  {marker} {command.Name} => {command.Command}";
        }).ToArray();

        var output = string.Join(
            Environment.NewLine,
            [
                $"Plugin: {plugin.Name}",
                $"Id: {plugin.Id}",
                $"Path: {plugin.Path}",
                $"Enabled: {plugin.Enabled}",
                $"Valid: {plugin.IsValid}",
                $"Health: {health.HealthStatus} (hooks: {health.HookSuccessCount} succeeded, {health.HookFailureCount} failed)",
                $"",
                $"Invocation Summary:",
                $"  Tools: {health.ToolInvocationCount} (last: {(health.LastToolInvocationUtc.HasValue ? health.LastToolInvocationUtc.Value.ToString("HH:mm:ss") : "never")})",
                $"  Commands: {health.CommandInvocationCount} (last: {(health.LastCommandInvocationUtc.HasValue ? health.LastCommandInvocationUtc.Value.ToString("HH:mm:ss") : "never")})",
                $"  Hooks: {health.HookInvocationCount} (last: {(health.LastActivityUtc.HasValue ? health.LastActivityUtc.Value.ToString("HH:mm:ss") : "never")})",
                $"",
                $"Tools ({plugin.Tools.Count}):",
                .. toolDetails.Any() ? toolDetails : ["  (none)"],
                $"",
                $"Commands ({plugin.Commands.Count}):",
                .. commandDetails.Any() ? commandDetails : ["  (none)"],
                $"",
                $"Hooks ({plugin.Hooks.Count}):",
                .. hookDetails.Any() ? hookDetails : ["  (none)"],
                $"",
                $"Errors: {(plugin.Errors.Count == 0 ? "(none)" : string.Join(Environment.NewLine, plugin.Errors.Select(error => $"  {error.Code}: {error.Message}")))}"
            ]);
        return CommandExecutionResult.Success(output);
    }
}
