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

    [Fact]
    public async Task Query_engine_streams_text_deltas_and_commits_final_message()
    {
        var store = new JsonSessionStore(_dataRoot);
        var client = new FakeAnthropicMessageClient();
        client.EnqueueStream(
            new MessageStartedEvent("claude-sonnet-4-5"),
            new TextDeltaEvent("hel"),
            new TextDeltaEvent("lo"),
            new TextCompletedEvent("hello"),
            new MessageCompletedEvent("end_turn"));
        var registry = new ToolRegistry([new EchoTool()]);
        var executor = new ToolExecutor(registry);
        var engine = new QueryEngine(store, client, registry, executor, new DefaultPermissionService());

        var events = new List<QueryStreamEvent>();
        await foreach (var streamEvent in engine.StreamAskAsync(new QueryRequest("hello"), CancellationToken.None))
        {
            events.Add(streamEvent);
        }

        Assert.Collection(
            events.OfType<AssistantTextDeltaStreamEvent>(),
            first => Assert.Equal("hel", first.DeltaText),
            second => Assert.Equal("lo", second.DeltaText));
        Assert.Contains(events, streamEvent => streamEvent is AssistantMessageCommittedEvent committed && committed.MessageText == "hello");
        Assert.Contains(events, streamEvent => streamEvent is TurnCompletedStreamEvent completed && completed.Result.AssistantText == "hello");
    }

    [Fact]
    public async Task Query_engine_does_not_persist_incomplete_assistant_message_when_canceled()
    {
        var store = new JsonSessionStore(_dataRoot);
        var client = new FakeAnthropicMessageClient();
        client.EnqueueStream(
            new MessageStartedEvent("claude-sonnet-4-5"),
            new TextDeltaEvent("partial"));
        var registry = new ToolRegistry([new EchoTool()]);
        var executor = new ToolExecutor(registry);
        var engine = new QueryEngine(store, client, registry, executor, new DefaultPermissionService());
        using var cancellation = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var streamEvent in engine.StreamAskAsync(new QueryRequest("hello"), cancellation.Token))
            {
                if (streamEvent is AssistantTextDeltaStreamEvent)
                {
                    cancellation.Cancel();
                }
            }
        });

        var session = (await store.ListAsync(CancellationToken.None)).Single();
        Assert.DoesNotContain(session.Messages, message => message.Role == "assistant" && message.Content == "partial");
    }

    [Fact]
    public async Task Query_engine_can_start_background_task_and_emit_task_started_event()
    {
        var store = new JsonSessionStore(_dataRoot);
        var taskStore = new JsonTaskStore(_dataRoot);
        var backgroundEngine = new FakeQueryEngine
        {
            Handler = async request =>
            {
                var workerSession = await store.GetAsync(request.SessionId!, CancellationToken.None)
                    ?? throw new InvalidOperationException("Expected worker session.");
                var updated = workerSession with
                {
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                    Messages =
                    [
                        .. workerSession.Messages,
                        new ConversationMessage("assistant", "worker done", DateTimeOffset.UtcNow)
                    ]
                };
                await store.SaveAsync(updated, CancellationToken.None);
                return new QueryExecutionResult(updated, "worker done", 1);
            }
        };
        var taskManager = new Runtime.Tasks.TaskManager(taskStore, store, backgroundEngine);
        await taskManager.InitializeAsync(CancellationToken.None);
        var client = new FakeAnthropicMessageClient(
            new ModelResponse(
                "claude-sonnet-4-5",
                [
                    new ToolUseContentBlock(
                        "tool-1",
                        "task_start",
                        new JsonObject
                        {
                            ["title"] = "Index repo",
                            ["goal"] = "Scan the repository"
                        })
                ],
                "tool_use"),
            new ModelResponse("claude-sonnet-4-5", [new TextContentBlock("task queued")], "end_turn"));
        var registry = new ToolRegistry([new TaskStartTool(taskManager)]);
        var executor = new ToolExecutor(registry);
        var engine = new QueryEngine(store, client, registry, executor, new DefaultPermissionService());

        var events = new List<QueryStreamEvent>();
        await foreach (var streamEvent in engine.StreamAskAsync(new QueryRequest("start task", null, null, 8, PermissionMode.BypassPermissions), CancellationToken.None))
        {
            events.Add(streamEvent);
        }

        Assert.Contains(events, streamEvent => streamEvent is TaskStartedStreamEvent started && started.Task.Title == "Index repo");
        var tasks = await taskStore.ListAsync(CancellationToken.None);
        Assert.Single(tasks);
        Assert.Equal("Index repo", tasks[0].Title);
    }

    [Fact]
    public async Task Query_engine_records_plugin_hook_messages_and_events()
    {
        var store = new JsonSessionStore(_dataRoot);
        var client = new FakeAnthropicMessageClient(
            new ModelResponse(
                "claude-sonnet-4-5",
                [new ToolUseContentBlock("tool-1", "echo", new JsonObject { ["text"] = "from-tool" })],
                "tool_use"),
            new ModelResponse("claude-sonnet-4-5", [new TextContentBlock("done")], "end_turn"));
        var registry = new ToolRegistry([new EchoTool()]);
        var executor = new ToolExecutor(registry);
        var pluginRuntime = new FakePluginRuntime
        {
            HookHandler = invocation =>
            {
                if (invocation.Kind == PluginHookKind.BeforeQuery)
                {
                    return
                    [
                        new PluginHookResult(
                            new PluginDefinition("demo", "demo", "/tmp/demo", true, null, []),
                            new PluginHookDefinition(PluginHookKind.BeforeQuery, "python3", [], new Dictionary<string, string>(), PluginExecutionMode.Subprocess, true, false),
                            true,
                            "before hook ok",
                            false)
                    ];
                }

                return [];
            }
        };
        var engine = new QueryEngine(store, client, registry, executor, new DefaultPermissionService(), pluginRuntime);

        var events = new List<QueryStreamEvent>();
        await foreach (var streamEvent in engine.StreamAskAsync(new QueryRequest("hello"), CancellationToken.None))
        {
            events.Add(streamEvent);
        }

        Assert.Contains(pluginRuntime.HookInvocations, invocation => invocation.Kind == PluginHookKind.BeforeQuery);
        Assert.Contains(events, streamEvent => streamEvent is PluginHookRecordedEvent hook && hook.Result.Message == "before hook ok");
        var session = (await store.ListAsync(CancellationToken.None)).Single();
        Assert.Contains(session.Messages, message => message.Role == "plugin_hook" && message.Content == "before hook ok");
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataRoot))
        {
            Directory.Delete(_dataRoot, recursive: true);
        }
    }
}
