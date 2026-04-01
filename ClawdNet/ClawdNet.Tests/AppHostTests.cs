using System.Text.Json;
using ClawdNet.App;
using ClawdNet.Core.Models;
using ClawdNet.Tests.TestDoubles;

namespace ClawdNet.Tests;

public sealed class AppHostTests : IDisposable
{
    private readonly string _dataRoot = Path.Combine(Path.GetTempPath(), "clawdnet-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Version_command_returns_product_version()
    {
        var host = new AppHost("1.2.3", _dataRoot, new FakeAnthropicMessageClient());

        var result = await host.RunAsync(["--version"], CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("1.2.3 (ClawdNet)", result.StdOut);
    }

    [Fact]
    public async Task No_args_launches_repl()
    {
        var replHost = new FakeReplHost();
        var host = new AppHost("1.0.0", _dataRoot, new FakeAnthropicMessageClient(), replHost: replHost);

        var result = await host.RunAsync([], CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Single(replHost.Launches);
    }

    [Fact]
    public async Task Session_new_creates_persisted_session()
    {
        var host = new AppHost("1.0.0", _dataRoot, new FakeAnthropicMessageClient());

        var createResult = await host.RunAsync(["session", "new", "Migration", "Slice"], CancellationToken.None);
        var listResult = await host.RunAsync(["session", "list"], CancellationToken.None);

        Assert.Equal(0, createResult.ExitCode);
        Assert.Contains("Created session", createResult.StdOut);
        Assert.Contains("Migration Slice", createResult.StdOut);
        Assert.Contains("Migration Slice", listResult.StdOut);
    }

    [Fact]
    public async Task Ask_creates_session_and_returns_assistant_text()
    {
        var client = new FakeAnthropicMessageClient(
            new ModelResponse("claude-sonnet-4-5", [new TextContentBlock("Hello from ClawdNet")], "end_turn"));
        var host = new AppHost("1.0.0", _dataRoot, client);

        var result = await host.RunAsync(["ask", "hello"], CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Session:", result.StdOut);
        Assert.Contains("Hello from ClawdNet", result.StdOut);
    }

    [Fact]
    public async Task Ask_json_emits_machine_readable_payload()
    {
        var client = new FakeAnthropicMessageClient(
            new ModelResponse("claude-sonnet-4-5", [new TextContentBlock("json-response")], "end_turn"));
        var host = new AppHost("1.0.0", _dataRoot, client);

        var result = await host.RunAsync(["ask", "--json", "hello"], CancellationToken.None);
        using var document = JsonDocument.Parse(result.StdOut);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("json-response", document.RootElement.GetProperty("assistantText").GetString());
        Assert.False(string.IsNullOrWhiteSpace(document.RootElement.GetProperty("sessionId").GetString()));
    }

    [Fact]
    public async Task Ask_with_missing_session_returns_stable_error()
    {
        var host = new AppHost("1.0.0", _dataRoot, new FakeAnthropicMessageClient());

        var result = await host.RunAsync(["ask", "--session", "missing", "hello"], CancellationToken.None);

        Assert.Equal(3, result.ExitCode);
        Assert.Contains("was not found", result.StdErr);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataRoot))
        {
            Directory.Delete(_dataRoot, recursive: true);
        }
    }
}
