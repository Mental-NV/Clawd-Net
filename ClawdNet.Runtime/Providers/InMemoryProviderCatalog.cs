using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Exceptions;
using ClawdNet.Core.Models;

namespace ClawdNet.Runtime.Providers;

public sealed class InMemoryProviderCatalog : IProviderCatalog
{
    private readonly IReadOnlyList<ProviderDefinition> _providers;
    private readonly string _defaultProvider;

    public InMemoryProviderCatalog(IReadOnlyList<ProviderDefinition>? providers = null, string defaultProvider = "anthropic")
    {
        _providers = providers?.ToArray() ?? ProviderDefaults.GetBuiltInProviders();
        _defaultProvider = defaultProvider;
    }

    public Task ReloadAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<IReadOnlyList<ProviderDefinition>> ListAsync(CancellationToken cancellationToken)
        => Task.FromResult(_providers);

    public Task<ProviderDefinition?> GetAsync(string providerName, CancellationToken cancellationToken)
        => Task.FromResult(_providers.FirstOrDefault(provider => string.Equals(provider.Name, providerName, StringComparison.OrdinalIgnoreCase)));

    public Task<ProviderDefinition> ResolveAsync(string? providerName, CancellationToken cancellationToken)
    {
        var resolvedName = string.IsNullOrWhiteSpace(providerName) ? _defaultProvider : providerName;
        var provider = _providers.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, resolvedName, StringComparison.OrdinalIgnoreCase) &&
            candidate.Enabled);
        if (provider is null)
        {
            throw new ModelProviderConfigurationException(resolvedName!, "Provider is not configured or enabled.");
        }

        return Task.FromResult(provider);
    }

}
