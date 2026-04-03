using System.Text.Json;
using ClawdNet.Core.Models;
using ClawdNet.Runtime.Protocols;
using ClawdNet.Tests.TestDoubles;

namespace ClawdNet.Tests;

public sealed class McpConfigurationLoaderTests : IDisposable
{
    private readonly string _dataRoot = Path.Combine(Path.GetTempPath(), "clawdnet-mcp-config", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Loader_reads_servers_from_json_config()
    {
        var configDirectory = Path.Combine(_dataRoot, "config");
        Directory.CreateDirectory(configDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(configDirectory, "mcp.json"),
            JsonSerializer.Serialize(new
            {
                servers = new[]
                {
                    new
                    {
                        name = "demo",
                        command = "python3",
                        arguments = new[] { "server.py" },
                        environment = new { DEMO = "1" },
                        enabled = true,
                        toolsReadOnly = true
                    }
                }
            }));
        var loader = new McpConfigurationLoader(_dataRoot);

        var configuration = await loader.LoadAsync(CancellationToken.None);

        Assert.Single(configuration.Servers);
        Assert.Equal("demo", configuration.Servers[0].Name);
        Assert.Equal("python3", configuration.Servers[0].Command);
        Assert.True(configuration.Servers[0].ToolsReadOnly);
        Assert.Equal("1", configuration.Servers[0].Environment["DEMO"]);
    }

    [Fact]
    public async Task Loader_merges_plugin_servers_with_local_config()
    {
        var configDirectory = Path.Combine(_dataRoot, "config");
        Directory.CreateDirectory(configDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(configDirectory, "mcp.json"),
            JsonSerializer.Serialize(new
            {
                servers = new[]
                {
                    new { name = "local", command = "python3" }
                }
            }));
        var pluginCatalog = new FakePluginCatalog
        {
            McpDefinitions =
            [
                new McpServerDefinition("plugin.demo", "python3", [], new Dictionary<string, string>(), true, true)
            ]
        };
        var loader = new McpConfigurationLoader(_dataRoot, pluginCatalog);

        var configuration = await loader.LoadAsync(CancellationToken.None);

        Assert.Equal(2, configuration.Servers.Count);
        Assert.Contains(configuration.Servers, server => server.Name == "local");
        Assert.Contains(configuration.Servers, server => server.Name == "plugin.demo");
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataRoot))
        {
            Directory.Delete(_dataRoot, true);
        }
    }
}
