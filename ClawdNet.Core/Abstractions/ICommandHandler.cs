using ClawdNet.Core.Models;

namespace ClawdNet.Core.Abstractions;

public interface ICommandHandler
{
    string Name { get; }

    string HelpSummary { get; }

    string HelpText { get; }

    bool CanHandle(CommandRequest request);

    Task<CommandExecutionResult> ExecuteAsync(CommandContext context, CommandRequest request, CancellationToken cancellationToken);
}
