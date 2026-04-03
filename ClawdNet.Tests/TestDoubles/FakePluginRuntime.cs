using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Models;

namespace ClawdNet.Tests.TestDoubles;

public sealed class FakePluginRuntime : IPluginRuntime
{
    public List<PluginHookInvocation> HookInvocations { get; } = [];

    public List<CommandRequest> CommandInvocations { get; } = [];

    public List<PluginToolInvocation> ToolInvocations { get; } = [];

    public Func<PluginHookInvocation, IReadOnlyList<PluginHookResult>> HookHandler { get; set; } = _ => [];

    public Func<CommandRequest, PluginCommandResult?> CommandHandler { get; set; } = _ => null;

    public Func<PluginToolInvocation, ToolExecutionResult> ToolHandler { get; set; } = _ => new ToolExecutionResult(false, string.Empty, "plugin tool unavailable");

    public int ReloadCount { get; private set; }

    public Task ReloadAsync(CancellationToken cancellationToken)
    {
        ReloadCount++;
        return Task.CompletedTask;
    }

    public Task<PluginCommandResult?> TryExecuteCommandAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        CommandInvocations.Add(request);
        return Task.FromResult(CommandHandler(request));
    }

    public Task<IReadOnlyList<PluginHookResult>> InvokeHooksAsync(PluginHookInvocation invocation, CancellationToken cancellationToken)
    {
        HookInvocations.Add(invocation);
        return Task.FromResult(HookHandler(invocation));
    }

    public Task<ToolExecutionResult> ExecuteToolAsync(PluginToolInvocation invocation, CancellationToken cancellationToken)
    {
        ToolInvocations.Add(invocation);
        return Task.FromResult(ToolHandler(invocation));
    }

    public PluginHealthMetrics GetHealthMetrics(string pluginName)
        => new PluginHealthMetrics();

    public IReadOnlyDictionary<string, PluginHealthMetrics> GetAllHealthMetrics()
        => new Dictionary<string, PluginHealthMetrics>();
}
