using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Exceptions;
using ClawdNet.Core.Models;

namespace ClawdNet.Tests.TestDoubles;

public sealed class FakeProviderCatalog : IProviderCatalog
{
    public List<ProviderDefinition> Providers { get; } =
    [
        new ProviderDefinition(ProviderDefaults.DefaultProviderName, ProviderKind.Anthropic, true, "ANTHROPIC_API_KEY", DefaultModel: ProviderDefaults.DefaultAnthropicModel),
        new ProviderDefinition("openai", ProviderKind.OpenAI, true, "OPENAI_API_KEY")
    ];

    public string DefaultProviderName { get; set; } = ProviderDefaults.DefaultProviderName;

    public Task ReloadAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<IReadOnlyList<ProviderDefinition>> ListAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<ProviderDefinition>>(Providers.ToArray());

    public Task<ProviderDefinition?> GetAsync(string providerName, CancellationToken cancellationToken)
        => Task.FromResult(Providers.FirstOrDefault(provider => string.Equals(provider.Name, providerName, StringComparison.OrdinalIgnoreCase)));

    public Task<ProviderDefinition> ResolveAsync(string? providerName, CancellationToken cancellationToken)
    {
        var resolvedName = string.IsNullOrWhiteSpace(providerName) ? DefaultProviderName : providerName;
        var provider = Providers.FirstOrDefault(candidate =>
            candidate.Enabled && string.Equals(candidate.Name, resolvedName, StringComparison.OrdinalIgnoreCase));
        if (provider is null)
        {
            throw new ModelProviderConfigurationException(resolvedName!, "Provider is not configured or enabled.");
        }

        return Task.FromResult(provider);
    }
}
