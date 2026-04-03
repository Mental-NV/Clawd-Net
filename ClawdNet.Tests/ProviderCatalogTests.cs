using System.Text.Json;
using ClawdNet.Core.Models;
using ClawdNet.Runtime.Providers;

namespace ClawdNet.Tests;

public sealed class ProviderCatalogTests : IDisposable
{
    private readonly string _dataRoot = Path.Combine(Path.GetTempPath(), "clawdnet-provider-tests", Guid.NewGuid().ToString("N"));

    public ProviderCatalogTests()
    {
        Directory.CreateDirectory(_dataRoot);
    }

    [Fact]
    public async Task Provider_catalog_uses_built_in_defaults_when_config_is_missing()
    {
        var catalog = new ProviderCatalog(_dataRoot);

        var providers = await catalog.ListAsync(CancellationToken.None);
        var defaultProvider = await catalog.ResolveAsync(null, CancellationToken.None);

        Assert.Contains(providers, provider => provider.Name == "anthropic");
        Assert.Contains(providers, provider => provider.Name == "openai");
        Assert.Contains(providers, provider => provider.Name == "bedrock");
        Assert.Contains(providers, provider => provider.Name == "vertex");
        Assert.Contains(providers, provider => provider.Name == "foundry");
        Assert.Equal("anthropic", defaultProvider.Name);
        Assert.Equal(ProviderDefaults.DefaultAnthropicModel, defaultProvider.DefaultModel);
    }

    [Fact]
    public async Task Provider_catalog_loads_configured_default_and_models()
    {
        var configRoot = Path.Combine(_dataRoot, "config");
        Directory.CreateDirectory(configRoot);
        var json = """
        {
          "defaultProvider": "openai",
          "providers": [
            {
              "name": "anthropic",
              "kind": "Anthropic",
              "enabled": true,
              "apiKeyEnv": "ANTHROPIC_API_KEY",
              "defaultModel": "claude-sonnet-4-5"
            },
            {
              "name": "openai",
              "kind": "OpenAI",
              "enabled": true,
              "apiKeyEnv": "OPENAI_API_KEY",
              "baseUrl": "https://api.example.com",
              "defaultModel": "gpt-4o-mini"
            }
          ]
        }
        """;
        await File.WriteAllTextAsync(Path.Combine(configRoot, "providers.json"), json);
        var catalog = new ProviderCatalog(_dataRoot);

        var provider = await catalog.ResolveAsync(null, CancellationToken.None);
        var openAi = await catalog.GetAsync("openai", CancellationToken.None);

        Assert.Equal("openai", provider.Name);
        Assert.Equal("gpt-4o-mini", provider.DefaultModel);
        Assert.NotNull(openAi);
        Assert.Equal("https://api.example.com", openAi!.BaseUrl);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataRoot))
        {
            Directory.Delete(_dataRoot, recursive: true);
        }
    }
}
