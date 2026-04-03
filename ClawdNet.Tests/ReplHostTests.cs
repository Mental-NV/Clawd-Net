using ClawdNet.Core.Models;
using ClawdNet.Runtime.Tools;
using ClawdNet.Runtime.Sessions;
using ClawdNet.Terminal.Models;
using ClawdNet.Terminal.Rendering;
using ClawdNet.Terminal.Repl;
using ClawdNet.Tests.TestDoubles;
using ClawdTaskStatus = ClawdNet.Core.Models.TaskStatus;

namespace ClawdNet.Tests;

public sealed class ReplHostTests : IDisposable
{
    private readonly string _dataRoot = Path.Combine(Path.GetTempPath(), "clawdnet-repl-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Repl_starts_new_session_processes_prompt_and_exits()
    {
        var store = new JsonSessionStore(_dataRoot);
        var terminal = new FakeTerminalSession(["hello", "exit"]);
        var queryEngine = new FakeQueryEngine
        {
            Handler = async request =>
            {
                var session = await store.GetAsync(request.SessionId!, CancellationToken.None)
                    ?? throw new InvalidOperationException("Expected session to exist.");
                var updated = session with
                {
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                    Messages =
                    [
                        .. session.Messages,
                        new ConversationMessage("user", request.Prompt, DateTimeOffset.UtcNow),
                        new ConversationMessage("assistant", "hi there", DateTimeOffset.UtcNow)
                    ]
                };
                await store.SaveAsync(updated, CancellationToken.None);
                return new QueryExecutionResult(updated, "hi there", 1);
            }
        };
        var host = new ReplHost(terminal, store, queryEngine, new ConsoleTranscriptRenderer(), new FakePtyManager(), new FakeTaskManager());

        var result = await host.RunAsync(new ReplLaunchOptions(), CancellationToken.None);
        var sessions = await store.ListAsync(CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Single(queryEngine.Requests);
        Assert.Equal("ClawdNet interactive mode", terminal.RenderedViews[0].Header);
        Assert.Contains("hi there", string.Join(Environment.NewLine, terminal.RenderedViews.Select(view => view.Transcript)));
        Assert.Equal("Exiting ClawdNet.", terminal.RenderedViews.Last().Activity);
        Assert.Single(sessions);
    }

    [Fact]
    public async Task Repl_resumes_existing_session()
    {
        var store = new JsonSessionStore(_dataRoot);
        var existing = await store.CreateAsync("Resume me", "claude-sonnet-4-5", CancellationToken.None);
        var terminal = new FakeTerminalSession(["quit"]);
        var queryEngine = new FakeQueryEngine();
        var host = new ReplHost(terminal, store, queryEngine, new ConsoleTranscriptRenderer(), new FakePtyManager(), new FakeTaskManager());

        var result = await host.RunAsync(new ReplLaunchOptions(existing.Id), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(queryEngine.Requests);
        Assert.Contains(existing.Id, terminal.RenderedViews[0].Footer);
    }

    [Fact]
    public async Task Repl_invalid_session_returns_failure()
    {
        var store = new JsonSessionStore(_dataRoot);
        var terminal = new FakeTerminalSession([]);
        var queryEngine = new FakeQueryEngine();
        var host = new ReplHost(terminal, store, queryEngine, new ConsoleTranscriptRenderer(), new FakePtyManager(), new FakeTaskManager());

        var result = await host.RunAsync(new ReplLaunchOptions("missing"), CancellationToken.None);

        Assert.Equal(3, result.ExitCode);
        Assert.Contains("was not found", result.StdErr);
    }

    [Fact]
    public async Task Repl_prompts_for_permission_and_continues_after_denial()
    {
        var store = new JsonSessionStore(_dataRoot);
        var session = await store.CreateAsync("Resume me", "claude-sonnet-4-5", CancellationToken.None);
        var terminal = new FakeTerminalSession(["hello", "quit"], [false]);
        var queryEngine = new FakeQueryEngine
        {
            Handler = async request =>
            {
                var allowed = await request.ApprovalHandler!.ApproveAsync(
                    new FileWriteTool(),
                    new ToolCall("tool-1", "file_write", null),
                    new PermissionDecision(PermissionDecisionKind.Ask, "Write tool requires approval."),
                    CancellationToken.None);
                var existing = await store.GetAsync(request.SessionId!, CancellationToken.None) ?? session;
                var updated = existing with
                {
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                    Messages =
                    [
                        .. existing.Messages,
                        new ConversationMessage("user", request.Prompt, DateTimeOffset.UtcNow),
                        new ConversationMessage("permission", allowed ? "Allow: user approved tool execution." : "Deny: user denied tool execution.", DateTimeOffset.UtcNow, "file_write", "tool-1", !allowed),
                        new ConversationMessage("assistant", allowed ? "approved" : "denied", DateTimeOffset.UtcNow)
                    ]
                };
                await store.SaveAsync(updated, CancellationToken.None);
                return new QueryExecutionResult(updated, allowed ? "approved" : "denied", 1);
            }
        };
        var host = new ReplHost(terminal, store, queryEngine, new ConsoleTranscriptRenderer(), new FakePtyManager(), new FakeTaskManager());

        var result = await host.RunAsync(new ReplLaunchOptions(session.Id), CancellationToken.None);
        var updatedSession = await store.GetAsync(session.Id, CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(terminal.Prompts, prompt => prompt.Contains("Allow file_write"));
        Assert.Contains(terminal.RenderedViews, view => view.Activity?.Contains("Awaiting approval", StringComparison.OrdinalIgnoreCase) == true);
        Assert.NotNull(updatedSession);
        Assert.Contains(updatedSession!.Messages, message => message.Role == "permission" && message.ToolName == "file_write" && message.IsError);
        Assert.Contains(updatedSession.Messages, message => message.Role == "assistant" && message.Content == "denied");
    }

    [Fact]
    public async Task Repl_supports_help_and_session_slash_commands()
    {
        var store = new JsonSessionStore(_dataRoot);
        var session = await store.CreateAsync("Session info", "claude-sonnet-4-5", CancellationToken.None);
        var terminal = new FakeTerminalSession(["/help", "/session", "/quit"]);
        var host = new ReplHost(terminal, store, new FakeQueryEngine(), new ConsoleTranscriptRenderer(), new FakePtyManager(), new FakeTaskManager());

        var result = await host.RunAsync(new ReplLaunchOptions(session.Id), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(terminal.RenderedViews, view => view.Activity?.Contains("Commands:", StringComparison.OrdinalIgnoreCase) == true);
        Assert.Contains(terminal.RenderedViews, view => view.Activity?.Contains(session.Id, StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public async Task Repl_supports_prompt_history_recall()
    {
        var store = new JsonSessionStore(_dataRoot);
        var terminal = new FakeTerminalSession([]);
        terminal.EnqueuePromptEvent(PromptInputResult.Submit("first prompt"));
        terminal.EnqueuePromptEvent(PromptInputResult.HistoryPrevious());
        terminal.EnqueuePromptEvent(PromptInputResult.Submit("exit"));
        var queryEngine = new FakeQueryEngine
        {
            Handler = async request =>
            {
                var session = await store.GetAsync(request.SessionId!, CancellationToken.None)
                    ?? throw new InvalidOperationException("Expected session to exist.");
                var updated = session with
                {
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                    Messages =
                    [
                        .. session.Messages,
                        new ConversationMessage("user", request.Prompt, DateTimeOffset.UtcNow),
                        new ConversationMessage("assistant", "done", DateTimeOffset.UtcNow)
                    ]
                };
                await store.SaveAsync(updated, CancellationToken.None);
                return new QueryExecutionResult(updated, "done", 1);
            }
        };
        var host = new ReplHost(terminal, store, queryEngine, new ConsoleTranscriptRenderer(), new FakePtyManager(), new FakeTaskManager());

        var result = await host.RunAsync(new ReplLaunchOptions(), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(terminal.RenderedViews, view => view.PromptBuffer == "first prompt");
    }

    [Fact]
    public async Task Repl_clear_clears_visible_screen_without_deleting_history()
    {
        var store = new JsonSessionStore(_dataRoot);
        var session = await store.CreateAsync("Clear me", "claude-sonnet-4-5", CancellationToken.None);
        var saved = session with
        {
            Messages =
            [
                new ConversationMessage("assistant", "existing", DateTimeOffset.UtcNow)
            ]
        };
        await store.SaveAsync(saved, CancellationToken.None);
        var terminal = new FakeTerminalSession(["/clear", "quit"]);
        var host = new ReplHost(terminal, store, new FakeQueryEngine(), new ConsoleTranscriptRenderer(), new FakePtyManager(), new FakeTaskManager());

        var result = await host.RunAsync(new ReplLaunchOptions(session.Id), CancellationToken.None);
        var reloaded = await store.GetAsync(session.Id, CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(1, terminal.ClearCount);
        Assert.Contains(terminal.RenderedViews, view => view.Activity?.Contains("history is preserved", StringComparison.OrdinalIgnoreCase) == true);
        Assert.NotNull(reloaded);
        Assert.Single(reloaded!.Messages);
    }

    [Fact]
    public async Task Repl_supports_slash_exit()
    {
        var store = new JsonSessionStore(_dataRoot);
        var terminal = new FakeTerminalSession(["/exit"]);
        var host = new ReplHost(terminal, store, new FakeQueryEngine(), new ConsoleTranscriptRenderer(), new FakePtyManager(), new FakeTaskManager());

        var result = await host.RunAsync(new ReplLaunchOptions(), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("Exiting ClawdNet.", terminal.RenderedViews.Last().Activity);
    }

    [Fact]
    public async Task Repl_renders_live_assistant_draft_during_streaming()
    {
        var store = new JsonSessionStore(_dataRoot);
        var terminal = new FakeTerminalSession(["hello", "exit"]);
        var queryEngine = new FakeQueryEngine
        {
            StreamHandler = request => StreamReplyAsync(request)
        };
        var host = new ReplHost(terminal, store, queryEngine, new ConsoleTranscriptRenderer(), new FakePtyManager(), new FakeTaskManager());

        var result = await host.RunAsync(new ReplLaunchOptions(), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(terminal.RenderedViews, view => view.Draft?.Contains("[live] ClawdNet hel", StringComparison.OrdinalIgnoreCase) == true);
        Assert.Contains(terminal.RenderedViews, view => view.Activity?.Contains("Streaming assistant response", StringComparison.OrdinalIgnoreCase) == true);
        Assert.Equal(string.Empty, terminal.RenderedViews.Last().Draft ?? string.Empty);
    }

    [Fact]
    public async Task Repl_interrupt_cancels_active_turn_without_exiting()
    {
        var store = new JsonSessionStore(_dataRoot);
        var terminal = new FakeTerminalSession(["hello", "exit"]);
        var queryEngine = new FakeQueryEngine
        {
            StreamHandler = request => InterruptibleStreamAsync(request, terminal)
        };
        var host = new ReplHost(terminal, store, queryEngine, new ConsoleTranscriptRenderer(), new FakePtyManager(), new FakeTaskManager());

        var result = await host.RunAsync(new ReplLaunchOptions(), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(terminal.RenderedViews, view => view.Activity?.Contains("Interrupted active turn", StringComparison.OrdinalIgnoreCase) == true);
        Assert.DoesNotContain(terminal.RenderedViews, view => view.Transcript.Contains("partial", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Repl_shows_edit_preview_before_batch_approval()
    {
        var store = new JsonSessionStore(_dataRoot);
        var terminal = new FakeTerminalSession(["hello", "exit"], [true]);
        var session = await store.CreateAsync("Interactive session", "claude-sonnet-4-5", CancellationToken.None);
        var preview = new EditPreview(true, new EditBatch([]), 1, "Edit batch touches 1 file(s): note.txt", "--- a.txt\n+++ a.txt\n@@\n-old\n+new");
        var queryEngine = new FakeQueryEngine
        {
            StreamHandler = _ => StreamEditPreviewAsync(session, preview)
        };
        var host = new ReplHost(terminal, store, queryEngine, new ConsoleTranscriptRenderer(), new FakePtyManager(), new FakeTaskManager());

        var result = await host.RunAsync(new ReplLaunchOptions(session.Id), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(terminal.RenderedViews, view => view.Draft?.Contains("--- a.txt", StringComparison.Ordinal) == true);
        Assert.Contains(terminal.RenderedViews, view => view.Activity?.Contains("Edit batch touches 1 file", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public async Task Repl_supports_pty_slash_command_and_renders_active_region()
    {
        var store = new JsonSessionStore(_dataRoot);
        var session = await store.CreateAsync("Interactive session", "claude-sonnet-4-5", CancellationToken.None);
        var terminal = new FakeTerminalSession(["/pty", "exit"]);
        var ptyManager = new FakePtyManager();
        ptyManager.Publish(FakePtyManager.NewState("cat", Environment.CurrentDirectory, "pty output", true, null, false));
        var host = new ReplHost(terminal, store, new FakeQueryEngine(), new ConsoleTranscriptRenderer(), ptyManager, new FakeTaskManager());

        var result = await host.RunAsync(new ReplLaunchOptions(session.Id), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(terminal.RenderedViews, view => view.Activity?.Contains("PTY", StringComparison.OrdinalIgnoreCase) == true);
        Assert.Contains(terminal.RenderedViews, view => view.Pty?.Contains("pty output", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public async Task Repl_supports_tasks_slash_command()
    {
        var store = new JsonSessionStore(_dataRoot);
        var session = await store.CreateAsync("Interactive session", "claude-sonnet-4-5", CancellationToken.None);
        var terminal = new FakeTerminalSession(["/tasks", "exit"]);
        var taskManager = new FakeTaskManager();
        var task = FakeTaskManager.NewTask(
            new TaskRequest("Index repo", "Scan the repository", session.Id),
            ClawdTaskStatus.Running,
            "Task started.");
        taskManager.Publish(task, task.Events!.Last());
        var host = new ReplHost(terminal, store, new FakeQueryEngine(), new ConsoleTranscriptRenderer(), new FakePtyManager(), taskManager);

        var result = await host.RunAsync(new ReplLaunchOptions(session.Id), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(terminal.RenderedViews, view => view.Activity?.Contains("Index repo", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public async Task Repl_supports_transcript_page_navigation()
    {
        var store = new JsonSessionStore(_dataRoot);
        var session = await store.CreateAsync("Scrollable", "claude-sonnet-4-5", CancellationToken.None);
        var saved = session with
        {
            Messages =
            [
                .. Enumerable.Range(0, 20).Select(index => new ConversationMessage("assistant", $"msg-{index}", DateTimeOffset.UtcNow))
            ]
        };
        await store.SaveAsync(saved, CancellationToken.None);
        var terminal = new FakeTerminalSession([]);
        terminal.EnqueuePromptEvent(PromptInputResult.ScrollPageUp());
        terminal.EnqueuePromptEvent(PromptInputResult.ScrollPageDown());
        terminal.EnqueuePromptEvent(PromptInputResult.Submit("exit"));
        var host = new ReplHost(terminal, store, new FakeQueryEngine(), new ConsoleTranscriptRenderer(), new FakePtyManager(), new FakeTaskManager());

        var result = await host.RunAsync(new ReplLaunchOptions(session.Id), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(terminal.RenderedViews, view =>
            view.Viewport?.FollowLiveOutput == false &&
            view.Transcript.Contains("msg-0", StringComparison.OrdinalIgnoreCase) &&
            !view.Transcript.Contains("msg-19", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(terminal.RenderedViews, view =>
            view.Viewport?.FollowLiveOutput == true &&
            view.Transcript.Contains("msg-19", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Repl_bottom_command_returns_to_live_output()
    {
        var store = new JsonSessionStore(_dataRoot);
        var session = await store.CreateAsync("Bottom", "claude-sonnet-4-5", CancellationToken.None);
        var saved = session with
        {
            Messages =
            [
                .. Enumerable.Range(0, 20).Select(index => new ConversationMessage("assistant", $"msg-{index}", DateTimeOffset.UtcNow))
            ]
        };
        await store.SaveAsync(saved, CancellationToken.None);
        var terminal = new FakeTerminalSession(["/bottom", "exit"]);
        terminal.EnqueuePromptEvent(PromptInputResult.ScrollPageUp());
        var host = new ReplHost(terminal, store, new FakeQueryEngine(), new ConsoleTranscriptRenderer(), new FakePtyManager(), new FakeTaskManager());

        var result = await host.RunAsync(new ReplLaunchOptions(session.Id), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(terminal.RenderedViews, view => view.Activity?.Contains("Returned to live output", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public async Task Repl_renders_live_task_updates_for_current_parent_session()
    {
        var store = new JsonSessionStore(_dataRoot);
        var session = await store.CreateAsync("Parent", "claude-sonnet-4-5", CancellationToken.None);
        var terminal = new FakeTerminalSession(["exit"])
        {
            ReadLineDelayMs = 100
        };
        var taskManager = new FakeTaskManager();
        var host = new ReplHost(terminal, store, new FakeQueryEngine(), new ConsoleTranscriptRenderer(), new FakePtyManager(), taskManager);

        var runTask = host.RunAsync(new ReplLaunchOptions(session.Id), CancellationToken.None);
        await Task.Delay(10);
        var timestamp = DateTimeOffset.UtcNow;
        var updatedSession = session with
        {
            UpdatedAtUtc = timestamp,
            Messages =
            [
                .. session.Messages,
                new ConversationMessage("task_completed", "Worker finished successfully.", timestamp, "task-1", "task-1")
            ]
        };
        await store.SaveAsync(updatedSession, CancellationToken.None);
        var task = new TaskRecord(
            "task-1",
            TaskKind.Worker,
            "Worker",
            "Goal",
            session.Id,
            "worker-session",
            "claude-sonnet-4-5",
            PermissionMode.Default,
            ClawdTaskStatus.Completed,
            timestamp,
            timestamp,
            timestamp,
            null,
            null,
            "Worker finished successfully.",
            new TaskResult(true, "Worker finished successfully."),
            [new TaskEvent(ClawdTaskStatus.Completed, "Worker finished successfully.", timestamp)]);
        taskManager.Publish(task, task.Events!.Last());
        var result = await runTask;

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(terminal.RenderedViews, view => view.Transcript.Contains("Worker finished successfully.", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(terminal.RenderedViews, view => view.Activity?.Contains("task-1", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public async Task Repl_interrupt_closes_active_pty_before_canceling_turn()
    {
        var store = new JsonSessionStore(_dataRoot);
        var terminal = new FakeTerminalSession(["exit"])
        {
            ReadLineDelayMs = 200
        };
        var ptyManager = new FakePtyManager();
        ptyManager.Publish(FakePtyManager.NewState("cat", Environment.CurrentDirectory, string.Empty, true, null, false));
        var host = new ReplHost(terminal, store, new FakeQueryEngine(), new ConsoleTranscriptRenderer(), ptyManager, new FakeTaskManager());

        var runTask = host.RunAsync(new ReplLaunchOptions(), CancellationToken.None);
        for (var attempt = 0; attempt < 50 && terminal.InterruptHandler is null; attempt++)
        {
            await Task.Delay(10);
        }
        terminal.TriggerInterrupt();
        var result = await runTask;

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(1, ptyManager.CloseCount);
    }

    private static async IAsyncEnumerable<QueryStreamEvent> StreamReplyAsync(QueryRequest request)
    {
        var session = new ConversationSession(
            request.SessionId ?? Guid.NewGuid().ToString("N"),
            "Interactive session",
            request.Model ?? "claude-sonnet-4-5",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            [new ConversationMessage("user", request.Prompt, DateTimeOffset.UtcNow)]);
        yield return new UserTurnAcceptedEvent(session);
        yield return new AssistantTextDeltaStreamEvent("hel");
        await Task.Yield();
        yield return new AssistantTextDeltaStreamEvent("lo");
        var completedSession = session with
        {
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            Messages =
            [
                .. session.Messages,
                new ConversationMessage("assistant", "hello", DateTimeOffset.UtcNow)
            ]
        };
        yield return new AssistantMessageCommittedEvent(completedSession, "hello");
        yield return new TurnCompletedStreamEvent(new QueryExecutionResult(completedSession, "hello", 1));
    }

    private static async IAsyncEnumerable<QueryStreamEvent> InterruptibleStreamAsync(QueryRequest request, FakeTerminalSession terminal)
    {
        var session = new ConversationSession(
            request.SessionId ?? Guid.NewGuid().ToString("N"),
            "Interactive session",
            request.Model ?? "claude-sonnet-4-5",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            [new ConversationMessage("user", request.Prompt, DateTimeOffset.UtcNow)]);
        yield return new UserTurnAcceptedEvent(session);
        yield return new AssistantTextDeltaStreamEvent("partial");
        await Task.Yield();
        terminal.TriggerInterrupt();
        await Task.Delay(10, CancellationToken.None);
        throw new OperationCanceledException();
    }

    private static async IAsyncEnumerable<QueryStreamEvent> StreamEditPreviewAsync(ConversationSession session, EditPreview preview)
    {
        var toolCall = new ToolCall("tool-1", "apply_patch", null);
        yield return new UserTurnAcceptedEvent(session);
        yield return new ToolCallRequestedEvent(toolCall);
        yield return new EditPreviewGeneratedEvent(session, toolCall, preview);
        yield return new EditApprovalRecordedEvent(session, toolCall, true, "Approved edit batch for application.");
        yield return new TurnCompletedStreamEvent(new QueryExecutionResult(session, string.Empty, 1));
        await Task.Yield();
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataRoot))
        {
            Directory.Delete(_dataRoot, recursive: true);
        }
    }
}
