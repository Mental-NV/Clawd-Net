using System.Text.Json;
using ClawdNet.Core.Models;
using ClawdNet.Runtime.Protocols;
using ClawdNet.Tests.TestDoubles;

namespace ClawdNet.Tests;

public sealed class LspConfigurationLoaderTests : IDisposable
{
    private readonly string _dataRoot = Path.Combine(Path.GetTempPath(), "clawdnet-lsp-config", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Loader_reads_servers_from_json_config()
    {
        var configDirectory = Path.Combine(_dataRoot, "config");
        Directory.CreateDirectory(configDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(configDirectory, "lsp.json"),
            JsonSerializer.Serialize(new
            {
                servers = new[]
                {
                    new
                    {
                        name = "csharp",
                        command = "python3",
                        arguments = new[] { "server.py" },
                        fileExtensions = new[] { ".cs", "csx" },
                        languageId = "csharp",
                        environment = new { DOTNET_ENVIRONMENT = "Development" },
                        enabled = true
                    }
                }
            }));
        var loader = new LspConfigurationLoader(_dataRoot);

        var configuration = await loader.LoadAsync(CancellationToken.None);

        Assert.Single(configuration.Servers);
        Assert.Equal("csharp", configuration.Servers[0].Name);
        Assert.Equal(".cs", configuration.Servers[0].FileExtensions[0]);
        Assert.Equal(".csx", configuration.Servers[0].FileExtensions[1]);
        Assert.Equal("Development", configuration.Servers[0].Environment["DOTNET_ENVIRONMENT"]);
    }

    [Fact]
    public async Task Loader_merges_plugin_servers_with_local_config()
    {
        var configDirectory = Path.Combine(_dataRoot, "config");
        Directory.CreateDirectory(configDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(configDirectory, "lsp.json"),
            JsonSerializer.Serialize(new
            {
                servers = new[]
                {
                    new { name = "local", command = "python3", fileExtensions = new[] { ".cs" } }
                }
            }));
        var pluginCatalog = new FakePluginCatalog
        {
            LspDefinitions =
            [
                new LspServerDefinition("plugin.csharp", "python3", [], new Dictionary<string, string>(), [".csx"], "csharp", true)
            ]
        };
        var loader = new LspConfigurationLoader(_dataRoot, pluginCatalog);

        var configuration = await loader.LoadAsync(CancellationToken.None);

        Assert.Equal(2, configuration.Servers.Count);
        Assert.Contains(configuration.Servers, server => server.Name == "local");
        Assert.Contains(configuration.Servers, server => server.Name == "plugin.csharp");
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataRoot))
        {
            Directory.Delete(_dataRoot, true);
        }
    }
}
