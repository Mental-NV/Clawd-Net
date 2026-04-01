using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Models;

namespace ClawdNet.Core.Services;

public sealed class CommandDispatcher
{
    private readonly IReadOnlyList<ICommandHandler> _handlers;

    public CommandDispatcher(IEnumerable<ICommandHandler> handlers)
    {
        _handlers = handlers.ToArray();
    }

    public async Task<CommandExecutionResult> DispatchAsync(
        CommandContext context,
        CommandRequest request,
        CancellationToken cancellationToken)
    {
        foreach (var handler in _handlers)
        {
            if (handler.CanHandle(request))
            {
                return await handler.ExecuteAsync(context, request, cancellationToken);
            }
        }

        var pluginCommand = await context.PluginRuntime.TryExecuteCommandAsync(request, cancellationToken);
        if (pluginCommand is not null)
        {
            return pluginCommand.Success
                ? new CommandExecutionResult(pluginCommand.ExitCode, pluginCommand.StdOut, pluginCommand.StdErr)
                : CommandExecutionResult.Failure(pluginCommand.StdErr, pluginCommand.ExitCode == 0 ? 1 : pluginCommand.ExitCode);
        }

        return CommandExecutionResult.Failure(
            "Unknown command. Supported commands: --version, ask <prompt>, session new, session list, task list, tool echo <text>, mcp list, lsp list, plugin list.");
    }
}
