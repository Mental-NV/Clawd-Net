using System.Text.Json;
using System.Text.Json.Nodes;
using ClawdNet.App;
using ClawdNet.Core.Models;
using ClawdNet.Tests.TestDoubles;

namespace ClawdNet.Tests;

public sealed class AppHostTests : IDisposable
{
    private readonly string _dataRoot = Path.Combine(Path.GetTempPath(), "clawdnet-tests", Guid.NewGuid().ToString("N"));

    public AppHostTests()
    {
        Directory.CreateDirectory(_dataRoot);
    }

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
    public async Task Ask_denies_apply_patch_in_default_mode_non_interactively()
    {
        var path = Path.Combine(_dataRoot, "default-note.txt");
        await File.WriteAllTextAsync(path, "hello");
        var client = new FakeAnthropicMessageClient(
            new ModelResponse(
                "claude-sonnet-4-5",
                [
                    new ToolUseContentBlock(
                        "tool-1",
                        "apply_patch",
                        new JsonObject
                        {
                            ["edits"] = new JsonArray
                            {
                                new JsonObject
                                {
                                    ["path"] = path,
                                    ["operation"] = "patch",
                                    ["hunks"] = new JsonArray
                                    {
                                        new JsonObject
                                        {
                                            ["oldText"] = "hello",
                                            ["newText"] = "hi"
                                        }
                                    }
                                }
                            }
                        })
                ],
                "tool_use"),
            new ModelResponse("claude-sonnet-4-5", [new TextContentBlock("patch denied")], "end_turn"));
        var host = new AppHost("1.0.0", _dataRoot, client);

        var result = await host.RunAsync(["ask", "edit the file"], CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("patch denied", result.StdOut);
        Assert.Equal("hello", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task Ask_accept_edits_applies_patch_batch_and_syncs_lsp()
    {
        var path = Path.Combine(_dataRoot, "accept-note.txt");
        await File.WriteAllTextAsync(path, "hello");
        var lspClient = new FakeLspClient();
        var client = new FakeAnthropicMessageClient(
            new ModelResponse(
                "claude-sonnet-4-5",
                [
                    new ToolUseContentBlock(
                        "tool-1",
                        "apply_patch",
                        new JsonObject
                        {
                            ["edits"] = new JsonArray
                            {
                                new JsonObject
                                {
                                    ["path"] = path,
                                    ["operation"] = "patch",
                                    ["hunks"] = new JsonArray
                                    {
                                        new JsonObject
                                        {
                                            ["oldText"] = "hello",
                                            ["newText"] = "hi"
                                        }
                                    }
                                }
                            }
                        })
                ],
                "tool_use"),
            new ModelResponse("claude-sonnet-4-5", [new TextContentBlock("patch applied")], "end_turn"));
        var host = new AppHost("1.0.0", _dataRoot, client, lspClient: lspClient);

        var result = await host.RunAsync(["ask", "--permission-mode", "accept-edits", "edit the file"], CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("patch applied", result.StdOut);
        Assert.Equal("hi", await File.ReadAllTextAsync(path));
        Assert.Single(lspClient.SyncRequests);
    }

    [Fact]
    public async Task Ask_can_round_trip_pty_tools_via_query_engine()
    {
        var ptyManager = new FakePtyManager();
        var client = new FakeAnthropicMessageClient(
            new ModelResponse(
                "claude-sonnet-4-5",
                [new ToolUseContentBlock("tool-1", "pty_start", new JsonObject { ["command"] = "cat" })],
                "tool_use"),
            new ModelResponse(
                "claude-sonnet-4-5",
                [new ToolUseContentBlock("tool-2", "pty_write", new JsonObject { ["text"] = "hello from pty\n" })],
                "tool_use"),
            new ModelResponse(
                "claude-sonnet-4-5",
                [new ToolUseContentBlock("tool-3", "pty_read", new JsonObject())],
                "tool_use"),
            new ModelResponse("claude-sonnet-4-5", [new TextContentBlock("used pty")], "end_turn"));
        var host = new AppHost("1.0.0", _dataRoot, client, ptyManager: ptyManager);

        var result = await host.RunAsync(["ask", "--permission-mode", "bypass-permissions", "use pty"], CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("used pty", result.StdOut);
        Assert.Single(ptyManager.Starts);
        Assert.Contains("hello from pty", ptyManager.CurrentState?.RecentOutput);
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

    [Fact]
    public async Task Plugin_list_reports_loaded_and_invalid_plugins()
    {
        var pluginCatalog = new FakePluginCatalog
        {
            Plugins =
            [
                new PluginDefinition(
                    "demo",
                    "demo",
                    "/tmp/demo",
                    true,
                    new PluginManifest("demo", "1.0.0", true, [], []),
                    []),
                new PluginDefinition(
                    "broken",
                    "broken",
                    "/tmp/broken",
                    false,
                    null,
                    [new PluginError("manifest-invalid", "bad json")])
            ]
        };
        var host = new AppHost("1.0.0", _dataRoot, new FakeAnthropicMessageClient(), pluginCatalog: pluginCatalog);

        var result = await host.RunAsync(["plugin", "list"], CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("demo", result.StdOut);
        Assert.Contains("broken", result.StdOut);
        Assert.Contains("manifest-invalid", result.StdOut);
    }

    [Fact]
    public async Task Plugin_reload_refreshes_catalog_and_dynamic_mcp_tools()
    {
        var pluginCatalog = new FakePluginCatalog
        {
            Plugins =
            [
                new PluginDefinition(
                    "demo",
                    "demo",
                    "/tmp/demo",
                    true,
                    new PluginManifest("demo", "1.0.0", true, [], []),
                    [])
            ]
        };
        var mcpClient = new FakeMcpClient
        {
            Servers = [],
            Tools = []
        };
        var lspClient = new FakeLspClient();
        pluginCatalog.ReloadHandler = _ =>
        {
            mcpClient.Servers =
            [
                new McpServerState("demo.echo", true, true, 1)
            ];
            mcpClient.Tools =
            [
                new McpToolDefinition(
                    "demo.echo",
                    "echo",
                    "Echo from plugin",
                    new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["text"] = new JsonObject { ["type"] = "string" }
                        }
                    },
                    true)
            ];
            return Task.CompletedTask;
        };
        var anthropicClient = new FakeAnthropicMessageClient(
            new ModelResponse(
                "claude-sonnet-4-5",
                [new ToolUseContentBlock("tool-1", "mcp.demo.echo.echo", new JsonObject { ["text"] = "hello" })],
                "tool_use"),
            new ModelResponse("claude-sonnet-4-5", [new TextContentBlock("reloaded plugin tool")], "end_turn"));
        var host = new AppHost("1.0.0", _dataRoot, anthropicClient, pluginCatalog: pluginCatalog, mcpClient: mcpClient, lspClient: lspClient);

        var reloadResult = await host.RunAsync(["plugin", "reload"], CancellationToken.None);
        var askResult = await host.RunAsync(["ask", "use reloaded plugin"], CancellationToken.None);

        Assert.Equal(0, reloadResult.ExitCode);
        Assert.Contains("Reloaded 1 plugin", reloadResult.StdOut);
        Assert.Equal(0, askResult.ExitCode);
        Assert.Contains("reloaded plugin tool", askResult.StdOut);
        Assert.Equal(2, pluginCatalog.ReloadCount);
        Assert.Equal(1, mcpClient.ReloadCount);
        Assert.Equal(1, lspClient.ReloadCount);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataRoot))
        {
            Directory.Delete(_dataRoot, recursive: true);
        }
    }
}
