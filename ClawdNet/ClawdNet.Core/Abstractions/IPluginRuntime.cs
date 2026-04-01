using ClawdNet.Core.Models;

namespace ClawdNet.Core.Abstractions;

public interface IPluginRuntime
{
    Task ReloadAsync(CancellationToken cancellationToken);

    Task<PluginCommandResult?> TryExecuteCommandAsync(CommandRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<PluginHookResult>> InvokeHooksAsync(PluginHookInvocation invocation, CancellationToken cancellationToken);
}
