namespace ClawdNet.Core.Models;

public sealed record PluginHookResult(
    PluginDefinition Plugin,
    PluginHookDefinition Hook,
    bool Success,
    string Message,
    bool Blocking,
    int ExitCode = 0);
