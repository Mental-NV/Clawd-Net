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

internal sealed class TerminalFallbackToolRegistry : IToolRegistry
{
    private readonly List<ITool> _tools = [];

    public IReadOnlyCollection<ITool> Tools => _tools.AsReadOnly();

    public bool TryGet(string name, out ITool? tool)
    {
        tool = _tools.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
        return tool is not null;
    }

    public void Register(ITool tool) => _tools.Add(tool);

    public void RegisterRange(IEnumerable<ITool> tools) => _tools.AddRange(tools);

    public void UnregisterWhere(Func<ITool, bool> predicate)
    {
        _tools.RemoveAll(new Predicate<ITool>(predicate));
    }
}
