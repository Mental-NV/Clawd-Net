using System.Text.Json;
using ClawdNet.Runtime.Plugins;

namespace ClawdNet.Tests;

public sealed class PluginCatalogTests : IDisposable
{
    private readonly string _dataRoot = Path.Combine(Path.GetTempPath(), "clawdnet-plugin-catalog", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Plugin_catalog_loads_valid_plugin_and_scopes_server_names()
    {
        var pluginRoot = Path.Combine(_dataRoot, "plugins", "demo");
        Directory.CreateDirectory(pluginRoot);
        await File.WriteAllTextAsync(
            Path.Combine(pluginRoot, "plugin.json"),
            JsonSerializer.Serialize(new
            {
                name = "demo",
                version = "1.0.0",
                enabled = true,
                mcpServers = new[]
                {
                    new
                    {
                        name = "echo",
                        command = "python3",
                        arguments = new[] { "server.py" },
                        toolsReadOnly = true
                    }
                },
                lspServers = new[]
                {
                    new
                    {
                        name = "csharp",
                        command = "python3",
                        arguments = new[] { "lsp.py" },
                        fileExtensions = new[] { ".cs" },
                        languageId = "csharp"
                    }
                },
                commands = new[]
                {
                    new
                    {
                        name = "demo-run",
                        command = "python3",
                        arguments = new[] { "command.py" }
                    }
                },
                hooks = new[]
                {
                    new
                    {
                        name = "before-query",
                        kind = "before-query",
                        command = "python3",
                        arguments = new[] { "hook.py" }
                    }
                }
            }));
        var catalog = new PluginCatalog(_dataRoot);

        await catalog.ReloadAsync(CancellationToken.None);

        Assert.Single(catalog.Plugins);
        Assert.True(catalog.Plugins[0].IsValid);
        Assert.Equal("demo.echo", catalog.Plugins[0].McpServers[0].Name);
        Assert.Equal("demo.csharp", catalog.Plugins[0].LspServers[0].Name);
        Assert.Contains(catalog.Plugins[0].Commands, command => command.Name == "demo-run");
        Assert.Contains(catalog.Plugins[0].Hooks, hook => hook.Kind == ClawdNet.Core.Models.PluginHookKind.BeforeQuery);
    }

    [Fact]
    public async Task Plugin_catalog_reports_invalid_manifest_without_crashing()
    {
        var pluginRoot = Path.Combine(_dataRoot, "plugins", "broken");
        Directory.CreateDirectory(pluginRoot);
        await File.WriteAllTextAsync(Path.Combine(pluginRoot, "plugin.json"), "{ invalid json");
        var catalog = new PluginCatalog(_dataRoot);

        await catalog.ReloadAsync(CancellationToken.None);

        Assert.Single(catalog.Plugins);
        Assert.False(catalog.Plugins[0].IsValid);
        Assert.Contains(catalog.Plugins[0].Errors, error => error.Code == "manifest-invalid");
    }

    [Fact]
    public async Task Plugin_catalog_reports_command_conflicts_without_invalidating_plugin()
    {
        var pluginRoot = Path.Combine(_dataRoot, "plugins", "demo");
        Directory.CreateDirectory(pluginRoot);
        await File.WriteAllTextAsync(
            Path.Combine(pluginRoot, "plugin.json"),
            JsonSerializer.Serialize(new
            {
                name = "demo",
                version = "1.0.0",
                enabled = true,
                commands = new[]
                {
                    new
                    {
                        name = "ask",
                        command = "python3"
                    },
                    new
                    {
                        name = "demo-ok",
                        command = "python3"
                    }
                }
            }));
        var catalog = new PluginCatalog(_dataRoot, ["ask"]);

        await catalog.ReloadAsync(CancellationToken.None);

        Assert.Single(catalog.Plugins);
        Assert.True(catalog.Plugins[0].IsValid);
        Assert.DoesNotContain(catalog.Plugins[0].Commands, command => command.Name == "ask");
        Assert.Contains(catalog.Plugins[0].Commands, command => command.Name == "demo-ok");
        Assert.Contains(catalog.Plugins[0].Errors, error => error.Code == "command-conflict");
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataRoot))
        {
            Directory.Delete(_dataRoot, true);
        }
    }
}
