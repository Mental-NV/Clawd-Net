using ClawdNet.Runtime.Platform;
using ClawdNet.Tests.TestDoubles;

namespace ClawdNet.Tests;

public sealed class DefaultPlatformLauncherTests : IDisposable
{
    private readonly string _dataRoot = Path.Combine(Path.GetTempPath(), "clawdnet-platform-tests", Guid.NewGuid().ToString("N"));

    public DefaultPlatformLauncherTests()
    {
        Directory.CreateDirectory(_dataRoot);
    }

    [Fact]
    public async Task Open_path_prefers_configured_editor_command()
    {
        var configRoot = Path.Combine(_dataRoot, "config");
        Directory.CreateDirectory(configRoot);
        await File.WriteAllTextAsync(
            Path.Combine(configRoot, "platform.json"),
            """
            {
              "editorCommand": "zed",
              "editorArguments": ["--wait"]
            }
            """);
        var runner = new FakeProcessRunner();
        var launcher = new DefaultPlatformLauncher(runner, new PlatformConfigurationLoader(_dataRoot));

        var result = await launcher.OpenPathAsync(new ClawdNet.Core.Models.PlatformOpenRequest("/tmp/demo.cs", 12, 3), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Single(runner.Requests);
        Assert.Equal("zed", runner.Requests[0].FileName);
        Assert.Contains("--wait", runner.Requests[0].Arguments);
        Assert.Contains("/tmp/demo.cs:12:3", runner.Requests[0].Arguments);
    }

    [Fact]
    public async Task Open_url_uses_browser_command_when_configured()
    {
        var configRoot = Path.Combine(_dataRoot, "config");
        Directory.CreateDirectory(configRoot);
        await File.WriteAllTextAsync(
            Path.Combine(configRoot, "platform.json"),
            """
            {
              "browserCommand": "browser",
              "browserArguments": ["--new-window"]
            }
            """);
        var runner = new FakeProcessRunner();
        var launcher = new DefaultPlatformLauncher(runner, new PlatformConfigurationLoader(_dataRoot));

        var result = await launcher.OpenUrlAsync("https://example.com", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Single(runner.Requests);
        Assert.Equal("browser", runner.Requests[0].FileName);
        Assert.Contains("https://example.com", runner.Requests[0].Arguments);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataRoot))
        {
            Directory.Delete(_dataRoot, recursive: true);
        }
    }
}
