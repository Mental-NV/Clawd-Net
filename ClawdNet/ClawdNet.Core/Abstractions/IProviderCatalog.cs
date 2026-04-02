using ClawdNet.Core.Models;

namespace ClawdNet.Core.Abstractions;

public interface IProviderCatalog
{
    Task ReloadAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<ProviderDefinition>> ListAsync(CancellationToken cancellationToken);

    Task<ProviderDefinition?> GetAsync(string providerName, CancellationToken cancellationToken);

    Task<ProviderDefinition> ResolveAsync(string? providerName, CancellationToken cancellationToken);
}
