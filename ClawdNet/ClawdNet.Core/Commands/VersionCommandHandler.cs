using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Metadata;
using ClawdNet.Core.Models;

namespace ClawdNet.Core.Commands;

public sealed class VersionCommandHandler : ICommandHandler
{
    public string Name => "version";

    public bool CanHandle(CommandRequest request)
    {
        return request.HasFlag("--version")
            || request.HasFlag("-v")
            || request.HasFlag("-V");
    }

    public Task<CommandExecutionResult> ExecuteAsync(
        CommandContext context,
        CommandRequest request,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(CommandExecutionResult.Success(
            $"{context.Version} ({AppMetadata.ProductName})"));
    }
}
