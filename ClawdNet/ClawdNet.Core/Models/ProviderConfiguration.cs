namespace ClawdNet.Core.Models;

public sealed record ProviderConfiguration(
    string DefaultProvider,
    IReadOnlyList<ProviderDefinition> Providers);
