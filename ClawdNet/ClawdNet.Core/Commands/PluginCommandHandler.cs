using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Models;
using ClawdNet.Core.Tools;

namespace ClawdNet.Core.Commands;

public sealed class PluginCommandHandler : ICommandHandler
{
    public string Name => "plugin";

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
            "list" => await ListAsync(context.PluginCatalog, cancellationToken),
            "show" => await ShowAsync(context.PluginCatalog, request, cancellationToken),
            "reload" => await ReloadAsync(context, cancellationToken),
            _ => CommandExecutionResult.Failure($"Unknown plugin subcommand '{request.Arguments[1]}'.")
        };
    }

    private static async Task<CommandExecutionResult> ListAsync(IPluginCatalog pluginCatalog, CancellationToken cancellationToken)
    {
        await pluginCatalog.ReloadAsync(cancellationToken);
        if (pluginCatalog.Plugins.Count == 0)
        {
            return CommandExecutionResult.Success("No plugins discovered.");
        }

        var lines = pluginCatalog.Plugins.Select(plugin =>
        {
            var errors = plugin.Errors.Count == 0
                ? string.Empty
                : $" | errors={string.Join("; ", plugin.Errors.Select(error => $"{error.Code}:{error.Message}"))}";
            return $"{plugin.Name} | enabled={plugin.Enabled} | valid={plugin.IsValid} | mcp={plugin.McpServers.Count} | lsp={plugin.LspServers.Count} | commands={plugin.Commands.Count} | hooks={plugin.Hooks.Count}{errors}";
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
        context.ToolRegistry.UnregisterWhere(tool => tool.Name.StartsWith("mcp.", StringComparison.OrdinalIgnoreCase));
        var tools = await context.McpClient.GetToolsAsync(null, cancellationToken);
        context.ToolRegistry.RegisterRange(tools.Select(tool => new McpToolProxy(context.McpClient, tool)));

        var output = string.Join(
            Environment.NewLine,
            [
                $"Reloaded {context.PluginCatalog.Plugins.Count} plugin(s).",
                $"MCP servers: {context.McpClient.Servers.Count}",
                $"LSP servers: {context.LspClient.Servers.Count}",
                $"Plugin commands: {context.PluginCatalog.Plugins.Sum(plugin => plugin.Commands.Count(command => command.Enabled))}",
                $"Plugin hooks: {context.PluginCatalog.Plugins.Sum(plugin => plugin.Hooks.Count(hook => hook.Enabled))}"
            ]);
        return CommandExecutionResult.Success(output);
    }
}
