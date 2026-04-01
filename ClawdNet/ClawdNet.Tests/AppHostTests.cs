using ClawdNet.App;

namespace ClawdNet.Tests;

public sealed class AppHostTests : IDisposable
{
    private readonly string _dataRoot = Path.Combine(Path.GetTempPath(), "clawdnet-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Version_command_returns_product_version()
    {
        var host = new AppHost("1.2.3", _dataRoot);

        var result = await host.RunAsync(["--version"], CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("1.2.3 (ClawdNet)", result.StdOut);
    }

    [Fact]
    public async Task Session_new_creates_persisted_session()
    {
        var host = new AppHost("1.0.0", _dataRoot);

        var createResult = await host.RunAsync(["session", "new", "Migration", "Slice"], CancellationToken.None);
        var listResult = await host.RunAsync(["session", "list"], CancellationToken.None);

        Assert.Equal(0, createResult.ExitCode);
        Assert.Contains("Created session", createResult.StdOut);
        Assert.Contains("Migration Slice", createResult.StdOut);
        Assert.Contains("Migration Slice", listResult.StdOut);
    }

    [Fact]
    public async Task Echo_tool_round_trips_payload()
    {
        var host = new AppHost("1.0.0", _dataRoot);

        var result = await host.RunAsync(["tool", "echo", "hello", "world"], CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("hello world", result.StdOut);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataRoot))
        {
            Directory.Delete(_dataRoot, recursive: true);
        }
    }
}
