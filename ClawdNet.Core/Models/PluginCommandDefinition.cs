namespace ClawdNet.Core.Models;

public sealed record PluginCommandDefinition(
    string Name,
    string Command,
    IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string> Environment,
    PluginExecutionMode ExecutionMode,
    bool Enabled);
