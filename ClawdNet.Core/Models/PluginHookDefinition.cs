namespace ClawdNet.Core.Models;

public sealed record PluginHookDefinition(
    PluginHookKind Kind,
    string Command,
    IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string> Environment,
    PluginExecutionMode ExecutionMode,
    bool Enabled,
    bool Blocking);
