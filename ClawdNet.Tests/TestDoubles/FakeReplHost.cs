using ClawdNet.Core.Models;
using ClawdNet.Terminal.Abstractions;

namespace ClawdNet.Tests.TestDoubles;

public sealed class FakeReplHost : IReplHost
{
    public List<ReplLaunchOptions> Launches { get; } = [];

    public Task<CommandExecutionResult> RunAsync(ReplLaunchOptions options, CancellationToken cancellationToken)
    {
        Launches.Add(options);
        return Task.FromResult(CommandExecutionResult.Success("repl"));
    }
}
