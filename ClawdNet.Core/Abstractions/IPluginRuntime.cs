using ClawdNet.Core.Models;

namespace ClawdNet.Core.Abstractions;

public interface IPluginRuntime
{
    Task ReloadAsync(CancellationToken cancellationToken);

    Task<PluginCommandResult?> TryExecuteCommandAsync(CommandRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<PluginHookResult>> InvokeHooksAsync(PluginHookInvocation invocation, CancellationToken cancellationToken);

    Task<ToolExecutionResult> ExecuteToolAsync(PluginToolInvocation invocation, CancellationToken cancellationToken);

    /// <summary>
    /// Gets health metrics for a specific plugin.
    /// </summary>
    PluginHealthMetrics GetHealthMetrics(string pluginName);

    /// <summary>
    /// Gets health metrics for all loaded plugins.
    /// </summary>
    IReadOnlyDictionary<string, PluginHealthMetrics> GetAllHealthMetrics();
}
