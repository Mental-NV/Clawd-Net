namespace ClawdNet.Core.Models;

public sealed record PluginCommandInvocation(
    PluginDefinition Plugin,
    PluginCommandDefinition Command,
    IReadOnlyList<string> Arguments,
    string? WorkingDirectory = null);
