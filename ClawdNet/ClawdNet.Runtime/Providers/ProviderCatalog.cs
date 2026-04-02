using System.Text.Json;
using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Exceptions;
using ClawdNet.Core.Models;

namespace ClawdNet.Runtime.Providers;

public sealed class ProviderCatalog : IProviderCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly string _dataRoot;
    private readonly SemaphoreSlim _sync = new(1, 1);
    private IReadOnlyList<ProviderDefinition>? _providers;
    private string? _defaultProvider;

    public ProviderCatalog(string dataRoot)
    {
        _dataRoot = dataRoot;
    }

    public async Task ReloadAsync(CancellationToken cancellationToken)
    {
        await _sync.WaitAsync(cancellationToken);
        try
        {
            (_defaultProvider, _providers) = await LoadConfigurationAsync(cancellationToken);
        }
        finally
        {
            _sync.Release();
        }
    }

    public async Task<IReadOnlyList<ProviderDefinition>> ListAsync(CancellationToken cancellationToken)
    {
        await EnsureLoadedAsync(cancellationToken);
        return _providers ?? [];
    }

    public async Task<ProviderDefinition?> GetAsync(string providerName, CancellationToken cancellationToken)
    {
        await EnsureLoadedAsync(cancellationToken);
        return (_providers ?? []).FirstOrDefault(provider => string.Equals(provider.Name, providerName, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<ProviderDefinition> ResolveAsync(string? providerName, CancellationToken cancellationToken)
    {
        await EnsureLoadedAsync(cancellationToken);
        var resolvedName = string.IsNullOrWhiteSpace(providerName) ? _defaultProvider : providerName;
        var provider = (_providers ?? []).FirstOrDefault(candidate =>
            string.Equals(candidate.Name, resolvedName, StringComparison.OrdinalIgnoreCase) &&
            candidate.Enabled);
        if (provider is null)
        {
            throw new ModelProviderConfigurationException(resolvedName ?? "provider", "Provider is not configured or enabled.");
        }

        return provider;
    }

    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (_providers is not null && !string.IsNullOrWhiteSpace(_defaultProvider))
        {
            return;
        }

        await ReloadAsync(cancellationToken);
    }

    private async Task<(string DefaultProvider, IReadOnlyList<ProviderDefinition> Providers)> LoadConfigurationAsync(CancellationToken cancellationToken)
    {
        var path = Path.Combine(_dataRoot, "config", "providers.json");
        if (!File.Exists(path))
        {
            var defaults = ProviderDefaults.GetBuiltInProviders();
            return (ProviderDefaults.DefaultProviderName, defaults);
        }

        await using var stream = File.OpenRead(path);
        var document = await JsonSerializer.DeserializeAsync<ProviderConfigurationDocument>(stream, JsonOptions, cancellationToken);
        var providers = (document?.Providers ?? [])
            .Select(ParseProvider)
            .Where(provider => provider is not null)
            .Cast<ProviderDefinition>()
            .ToArray();
        if (providers.Length == 0)
        {
            var defaults = ProviderDefaults.GetBuiltInProviders();
            return (ProviderDefaults.DefaultProviderName, defaults);
        }

        var defaultProvider = document?.DefaultProvider;
        if (string.IsNullOrWhiteSpace(defaultProvider) ||
            providers.All(provider => !provider.Enabled || !string.Equals(provider.Name, defaultProvider, StringComparison.OrdinalIgnoreCase)))
        {
            defaultProvider = providers.FirstOrDefault(provider => provider.Enabled)?.Name ?? ProviderDefaults.DefaultProviderName;
        }

        return (defaultProvider!, providers);
    }

    private static ProviderDefinition? ParseProvider(ProviderDefinitionDocument? document)
    {
        if (document is null || string.IsNullOrWhiteSpace(document.Name))
        {
            return null;
        }

        if (!Enum.TryParse<ProviderKind>(document.Kind, true, out var kind))
        {
            return null;
        }

        var apiKeyEnv = string.IsNullOrWhiteSpace(document.ApiKeyEnv)
            ? kind switch
            {
                ProviderKind.Anthropic => "ANTHROPIC_API_KEY",
                ProviderKind.OpenAI => "OPENAI_API_KEY",
                _ => "API_KEY"
            }
            : document.ApiKeyEnv!;

        return new ProviderDefinition(
            document.Name.Trim(),
            kind,
            document.Enabled ?? true,
            apiKeyEnv.Trim(),
            string.IsNullOrWhiteSpace(document.BaseUrl) ? null : document.BaseUrl.Trim(),
            string.IsNullOrWhiteSpace(document.DefaultModel) ? null : document.DefaultModel.Trim());
    }

    private sealed class ProviderConfigurationDocument
    {
        public string? DefaultProvider { get; init; }

        public List<ProviderDefinitionDocument>? Providers { get; init; }
    }

    private sealed class ProviderDefinitionDocument
    {
        public string? Name { get; init; }

        public string? Kind { get; init; }

        public bool? Enabled { get; init; }

        public string? ApiKeyEnv { get; init; }

        public string? BaseUrl { get; init; }

        public string? DefaultModel { get; init; }
    }
}
