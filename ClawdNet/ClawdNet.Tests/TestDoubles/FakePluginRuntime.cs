using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Models;

namespace ClawdNet.Tests.TestDoubles;

public sealed class FakePluginRuntime : IPluginRuntime
{
    public List<PluginHookInvocation> HookInvocations { get; } = [];

    public List<CommandRequest> CommandInvocations { get; } = [];

    public Func<PluginHookInvocation, IReadOnlyList<PluginHookResult>> HookHandler { get; set; } = _ => [];

    public Func<CommandRequest, PluginCommandResult?> CommandHandler { get; set; } = _ => null;

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
}
