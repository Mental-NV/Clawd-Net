namespace ClawdNet.Core.Models;

public sealed record PluginLoadResult(IReadOnlyList<PluginDefinition> Plugins);
