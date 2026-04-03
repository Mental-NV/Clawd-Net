using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Exceptions;
using ClawdNet.Core.Models;

namespace ClawdNet.Terminal.Defaults;

internal sealed class TerminalFallbackProviderCatalog : IProviderCatalog
{
    private readonly IReadOnlyList<ProviderDefinition> _providers = ProviderDefaults.GetBuiltInProviders();

    public Task ReloadAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<IReadOnlyList<ProviderDefinition>> ListAsync(CancellationToken cancellationToken)
        => Task.FromResult(_providers);

    public Task<ProviderDefinition?> GetAsync(string providerName, CancellationToken cancellationToken)
        => Task.FromResult(_providers.FirstOrDefault(provider => string.Equals(provider.Name, providerName, StringComparison.OrdinalIgnoreCase)));

    public Task<ProviderDefinition> ResolveAsync(string? providerName, CancellationToken cancellationToken)
    {
        var resolvedName = string.IsNullOrWhiteSpace(providerName) ? ProviderDefaults.DefaultProviderName : providerName;
        var provider = _providers.FirstOrDefault(candidate =>
            candidate.Enabled && string.Equals(candidate.Name, resolvedName, StringComparison.OrdinalIgnoreCase));
        if (provider is null)
        {
            throw new ModelProviderConfigurationException(resolvedName!, "Provider is not configured or enabled.");
        }

        return Task.FromResult(provider);
    }
}

internal sealed class TerminalNullPlatformLauncher : IPlatformLauncher
{
    public Task<PlatformLaunchResult> OpenPathAsync(PlatformOpenRequest request, CancellationToken cancellationToken)
        => Task.FromResult(new PlatformLaunchResult(false, string.Empty, "Platform launcher is unavailable."));

    public Task<PlatformLaunchResult> OpenUrlAsync(string url, CancellationToken cancellationToken)
        => Task.FromResult(new PlatformLaunchResult(false, string.Empty, "Platform launcher is unavailable."));
}
