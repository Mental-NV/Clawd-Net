using ClawdNet.Core.Models;

namespace ClawdNet.Terminal.Abstractions;

public interface ITuiHost
{
    Task<CommandExecutionResult> RunAsync(ReplLaunchOptions options, CancellationToken cancellationToken);
}
