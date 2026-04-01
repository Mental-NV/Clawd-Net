using System.Text.Json;
using ClawdNet.Runtime.Protocols;

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

    public void Dispose()
    {
        if (Directory.Exists(_dataRoot))
        {
            Directory.Delete(_dataRoot, true);
        }
    }
}
