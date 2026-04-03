using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Models;

namespace ClawdNet.Tests.TestDoubles;

public sealed class FakeModelClientFactory : IModelClientFactory
{
    public Dictionary<string, IModelClient> Clients { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> RequestedProviders { get; } = [];

    public IModelClient Create(ProviderDefinition provider)
    {
        RequestedProviders.Add(provider.Name);
        if (Clients.TryGetValue(provider.Name, out var client))
        {
            return client;
        }

        throw new InvalidOperationException($"No fake model client registered for provider '{provider.Name}'.");
    }
}
