using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Models;

namespace ClawdNet.Runtime.Plugins;

public sealed class NullPluginRuntime : IPluginRuntime
{
    public Task ReloadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task<PluginCommandResult?> TryExecuteCommandAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<PluginCommandResult?>(null);
    }

    public Task<IReadOnlyList<PluginHookResult>> InvokeHooksAsync(PluginHookInvocation invocation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<PluginHookResult>>([]);
    }

    public Task<ToolExecutionResult> ExecuteToolAsync(PluginToolInvocation invocation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new ToolExecutionResult(false, string.Empty, $"Plugin tool '{invocation.QualifiedToolName}' is unavailable."));
    }

    public PluginHealthMetrics GetHealthMetrics(string pluginName) => new();

    public IReadOnlyDictionary<string, PluginHealthMetrics> GetAllHealthMetrics() => new Dictionary<string, PluginHealthMetrics>();
}
