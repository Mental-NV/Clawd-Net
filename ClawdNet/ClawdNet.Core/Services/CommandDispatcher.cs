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

        return CommandExecutionResult.Failure(
            "Unknown command. Supported commands: --version, session new, session list, tool echo <text>.");
    }
}
