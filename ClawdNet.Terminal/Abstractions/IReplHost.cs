using ClawdNet.Core.Models;

namespace ClawdNet.Terminal.Abstractions;

public interface IReplHost
{
    Task<CommandExecutionResult> RunAsync(ReplLaunchOptions options, CancellationToken cancellationToken);
}
