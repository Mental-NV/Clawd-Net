using ClawdNet.Core.Models;
using ClawdNet.Terminal.Abstractions;

namespace ClawdNet.Tests.TestDoubles;

public sealed class FakeTuiHost : ITuiHost
{
    public List<ReplLaunchOptions> Launches { get; } = [];

    public Task<CommandExecutionResult> RunAsync(ReplLaunchOptions options, CancellationToken cancellationToken)
    {
        Launches.Add(options);
        return Task.FromResult(CommandExecutionResult.Success("tui"));
    }
}
