using System.Text.Json;
using System.Text.Json.Nodes;
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

    [Fact]
    public async Task Ask_accepts_permission_mode_flag()
    {
        var client = new FakeAnthropicMessageClient(
            new ModelResponse("claude-sonnet-4-5", [new TextContentBlock("permission-mode")], "end_turn"));
        var host = new AppHost("1.0.0", _dataRoot, client);

        var result = await host.RunAsync(["ask", "--permission-mode", "bypass-permissions", "hello"], CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("permission-mode", result.StdOut);
    }

    [Fact]
    public async Task Mcp_list_reports_server_state()
    {
        var mcpClient = new FakeMcpClient
        {
            Servers =
            [
                new McpServerState("demo", true, true, 1)
            ],
            Tools =
            [
                new McpToolDefinition("demo", "echo", "Echo from MCP", new JsonObject(), true)
            ]
        };
        var host = new AppHost("1.0.0", _dataRoot, new FakeAnthropicMessageClient(), mcpClient: mcpClient);

        var result = await host.RunAsync(["mcp", "list"], CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("demo", result.StdOut);
        Assert.Contains("connected=True", result.StdOut, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Ask_can_use_registered_mcp_tool()
    {
        var mcpClient = new FakeMcpClient
        {
            Servers =
            [
                new McpServerState("demo", true, true, 1)
            ],
            Tools =
            [
                new McpToolDefinition(
                    "demo",
                    "echo",
                    "Echo from MCP",
                    new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["text"] = new JsonObject { ["type"] = "string" }
                        }
                    },
                    true)
            ]
        };
        var anthropicClient = new FakeAnthropicMessageClient(
            new ModelResponse(
                "claude-sonnet-4-5",
                [new ToolUseContentBlock("tool-1", "mcp.demo.echo", new JsonObject { ["text"] = "hello" })],
                "tool_use"),
            new ModelResponse("claude-sonnet-4-5", [new TextContentBlock("used mcp")], "end_turn"));
        var host = new AppHost("1.0.0", _dataRoot, anthropicClient, mcpClient: mcpClient);

        var result = await host.RunAsync(["ask", "try mcp"], CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("used mcp", result.StdOut);
        Assert.Single(mcpClient.Invocations);
        Assert.Equal("demo", mcpClient.Invocations[0].ServerName);
        Assert.Equal("echo", mcpClient.Invocations[0].ToolName);
    }

    [Fact]
    public async Task Lsp_list_reports_server_state()
    {
        var lspClient = new FakeLspClient
        {
            Servers =
            [
                new LspServerState("csharp", true, true, [".cs"])
            ]
        };
        var host = new AppHost("1.0.0", _dataRoot, new FakeAnthropicMessageClient(), lspClient: lspClient);

        var result = await host.RunAsync(["lsp", "list"], CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("csharp", result.StdOut);
        Assert.Contains(".cs", result.StdOut);
    }

    [Fact]
    public async Task Ask_can_use_registered_lsp_tool()
    {
        var lspClient = new FakeLspClient
        {
            Servers =
            [
                new LspServerState("csharp", true, true, [".cs"])
            ],
            DefinitionsHandler = (path, line, character) =>
            [
                new LspLocation(path, line, character)
            ]
        };
        var anthropicClient = new FakeAnthropicMessageClient(
            new ModelResponse(
                "claude-sonnet-4-5",
                [new ToolUseContentBlock("tool-1", "lsp_definition", new JsonObject { ["path"] = "/tmp/a.cs", ["line"] = 1, ["character"] = 2 })],
                "tool_use"),
            new ModelResponse("claude-sonnet-4-5", [new TextContentBlock("used lsp")], "end_turn"));
        var host = new AppHost("1.0.0", _dataRoot, anthropicClient, lspClient: lspClient);

        var result = await host.RunAsync(["ask", "use lsp"], CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("used lsp", result.StdOut);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataRoot))
        {
            Directory.Delete(_dataRoot, recursive: true);
        }
    }
}
