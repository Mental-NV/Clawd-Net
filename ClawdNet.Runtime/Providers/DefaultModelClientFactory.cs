using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Models;
using ClawdNet.Runtime.Anthropic;
using ClawdNet.Runtime.Bedrock;
using ClawdNet.Runtime.Foundry;
using ClawdNet.Runtime.OpenAI;
using ClawdNet.Runtime.VertexAI;

namespace ClawdNet.Runtime.Providers;

public sealed class DefaultModelClientFactory : IModelClientFactory
{
    private readonly Func<HttpClient> _httpClientFactory;
    private readonly Dictionary<string, IModelClient> _overrides = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IModelClient> _cache = new(StringComparer.OrdinalIgnoreCase);

    public DefaultModelClientFactory(Func<HttpClient>? httpClientFactory = null)
    {
        _httpClientFactory = httpClientFactory ?? (() => new HttpClient());
    }

    public void RegisterOverride(string providerName, IModelClient client)
    {
        _overrides[providerName] = client;
        _cache.Remove(providerName);
    }

    public IModelClient Create(ProviderDefinition provider)
    {
        if (_overrides.TryGetValue(provider.Name, out var overrideClient))
        {
            return overrideClient;
        }

        if (_cache.TryGetValue(provider.Name, out var cached))
        {
            return cached;
        }

        var client = provider.Kind switch
        {
            ProviderKind.Anthropic => (IModelClient)new HttpAnthropicMessageClient(
                _httpClientFactory(),
                () => Environment.GetEnvironmentVariable(provider.ApiKeyEnvironmentVariable),
                provider.BaseUrl),
            ProviderKind.OpenAI => new HttpOpenAiMessageClient(
                _httpClientFactory(),
                () => Environment.GetEnvironmentVariable(provider.ApiKeyEnvironmentVariable),
                provider.BaseUrl),
            ProviderKind.Bedrock => new HttpBedrockMessageClient(
                _httpClientFactory(),
                provider.DefaultModel ?? string.Empty,
                new BedrockCredentialResolver()),
            ProviderKind.VertexAI => new HttpVertexAIMessageClient(
                _httpClientFactory(),
                provider.DefaultModel ?? "claude-sonnet-4-5",
                new VertexAICredentialResolver()),
            ProviderKind.Foundry => new HttpFoundryMessageClient(
                _httpClientFactory(),
                provider.DefaultModel ?? "claude-sonnet-4-5",
                new FoundryCredentialResolver()),
            _ => throw new InvalidOperationException($"Unsupported provider kind '{provider.Kind}'.")
        };
        _cache[provider.Name] = client;
        return client;
    }
}
