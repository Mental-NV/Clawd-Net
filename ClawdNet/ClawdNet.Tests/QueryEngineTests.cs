using System.Text.Json.Nodes;
using ClawdNet.Core.Models;
using ClawdNet.Core.Services;
using ClawdNet.Runtime.Permissions;
using ClawdNet.Runtime.Sessions;
using ClawdNet.Runtime.Tools;
using ClawdNet.Tests.TestDoubles;

namespace ClawdNet.Tests;

public sealed class QueryEngineTests : IDisposable
{
    private readonly string _dataRoot = Path.Combine(Path.GetTempPath(), "clawdnet-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Query_engine_executes_tool_loop_and_persists_messages()
    {
        var store = new JsonSessionStore(_dataRoot);
        var client = new FakeAnthropicMessageClient(
            new ModelResponse(
                "claude-sonnet-4-5",
                [new ToolUseContentBlock("tool-1", "echo", new JsonObject { ["text"] = "from-tool" })],
                "tool_use"),
            new ModelResponse(
                "claude-sonnet-4-5",
                [new TextContentBlock("tool completed")],
                "end_turn"));
        var registry = new ToolRegistry([new EchoTool()]);
        var executor = new ToolExecutor(registry);
        var engine = new QueryEngine(store, client, registry, executor, new DefaultPermissionService());

        var result = await engine.AskAsync(new QueryRequest("say hi"), CancellationToken.None);
        var savedSession = await store.GetAsync(result.Session.Id, CancellationToken.None);

        Assert.Equal("tool completed", result.AssistantText);
        Assert.NotNull(savedSession);
        Assert.Contains(savedSession!.Messages, message => message.Role == "tool_use");
        Assert.Contains(savedSession.Messages, message => message.Role == "permission");
        Assert.Contains(savedSession.Messages, message => message.Role == "tool_result" && message.Content == "from-tool");
        Assert.Contains(savedSession.Messages, message => message.Role == "assistant" && message.Content == "tool completed");
    }

    [Fact]
    public async Task Query_engine_can_resume_existing_session()
    {
        var store = new JsonSessionStore(_dataRoot);
        var client = new FakeAnthropicMessageClient(
            new ModelResponse("claude-sonnet-4-5", [new TextContentBlock("first")], "end_turn"),
            new ModelResponse("claude-sonnet-4-5", [new TextContentBlock("second")], "end_turn"));
        var registry = new ToolRegistry([new EchoTool()]);
        var executor = new ToolExecutor(registry);
        var engine = new QueryEngine(store, client, registry, executor, new DefaultPermissionService());

        var first = await engine.AskAsync(new QueryRequest("hello"), CancellationToken.None);
        var second = await engine.AskAsync(new QueryRequest("again", first.Session.Id), CancellationToken.None);

        Assert.Equal(first.Session.Id, second.Session.Id);
        Assert.True(second.Session.Messages.Count >= 5);
    }

    [Fact]
    public async Task Query_engine_denies_write_tool_in_default_mode_non_interactively()
    {
        var store = new JsonSessionStore(_dataRoot);
        var client = new FakeAnthropicMessageClient(
            new ModelResponse(
                "claude-sonnet-4-5",
                [new ToolUseContentBlock("tool-1", "file_write", new JsonObject { ["path"] = Path.Combine(_dataRoot, "a.txt"), ["content"] = "x" })],
                "tool_use"),
            new ModelResponse("claude-sonnet-4-5", [new TextContentBlock("write denied")], "end_turn"));
        var registry = new ToolRegistry([new FileWriteTool()]);
        var executor = new ToolExecutor(registry);
        var engine = new QueryEngine(store, client, registry, executor, new DefaultPermissionService());

        var result = await engine.AskAsync(new QueryRequest("write a file"), CancellationToken.None);

        Assert.Equal("write denied", result.AssistantText);
        Assert.Contains(result.Session.Messages, message => message.Role == "tool_result" && message.IsError && message.ToolName == "file_write");
    }

    [Fact]
    public async Task Query_engine_records_lsp_tool_failures_as_tool_results()
    {
        var store = new JsonSessionStore(_dataRoot);
        var client = new FakeAnthropicMessageClient(
            new ModelResponse(
                "claude-sonnet-4-5",
                [new ToolUseContentBlock("tool-1", "lsp_hover", new JsonObject { ["path"] = "/tmp/a.cs", ["line"] = 0, ["character"] = 0 })],
                "tool_use"),
            new ModelResponse("claude-sonnet-4-5", [new TextContentBlock("hover failed")], "end_turn"));
        var lspClient = new FakeLspClient
        {
            HoverHandler = (_, _, _) => throw new InvalidOperationException("hover exploded")
        };
        var registry = new ToolRegistry([new LspHoverTool(lspClient)]);
        var executor = new ToolExecutor(registry);
        var engine = new QueryEngine(store, client, registry, executor, new DefaultPermissionService());

        var result = await engine.AskAsync(new QueryRequest("hover"), CancellationToken.None);

        Assert.Equal("hover failed", result.AssistantText);
        Assert.Contains(result.Session.Messages, message => message.Role == "tool_result" && message.IsError && message.ToolName == "lsp_hover");
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataRoot))
        {
            Directory.Delete(_dataRoot, recursive: true);
        }
    }
}
