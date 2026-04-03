using ClawdNet.Core.Models;

namespace ClawdNet.Core.Abstractions;

public interface ICommandHandler
{
    string Name { get; }

    bool CanHandle(CommandRequest request);

    Task<CommandExecutionResult> ExecuteAsync(CommandContext context, CommandRequest request, CancellationToken cancellationToken);
}
