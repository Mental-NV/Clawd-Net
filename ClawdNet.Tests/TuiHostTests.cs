using ClawdNet.Core.Models;
using ClawdNet.Runtime.Sessions;
using ClawdNet.Terminal.Models;
using ClawdNet.Terminal.Rendering;
using ClawdNet.Terminal.Tui;
using ClawdNet.Tests.TestDoubles;

namespace ClawdNet.Tests;

public sealed class TuiHostTests : IDisposable
{
    private readonly string _dataRoot = Path.Combine(Path.GetTempPath(), "clawdnet-tui-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Tui_starts_new_session_processes_prompt_and_exits()
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
                        new ConversationMessage("assistant", "hi from tui", DateTimeOffset.UtcNow)
                    ]
                };
                await store.SaveAsync(updated, CancellationToken.None);
                return new QueryExecutionResult(updated, "hi from tui", 1);
            }
        };
        var host = new TuiHost(terminal, store, queryEngine, new ConsoleTuiRenderer(new ConsoleTranscriptRenderer()), new FakePtyManager(), new FakeTaskManager());

        var result = await host.RunAsync(new ReplLaunchOptions(), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(1, terminal.EnterAlternateScreenCount);
        Assert.Equal(1, terminal.LeaveAlternateScreenCount);
        Assert.Contains(terminal.RenderedFrames, frame => frame.Header.Contains("ClawdNet TUI", StringComparison.Ordinal));
        Assert.Contains(terminal.RenderedFrames, frame => frame.TranscriptPane.Contains("hi from tui", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Tui_toggles_help_overlay_and_session_overlay()
    {
        var store = new JsonSessionStore(_dataRoot);
        var terminal = new FakeTerminalSession(["exit"]);
        terminal.EnqueuePromptEvent(PromptInputResult.ToggleHelp());
        terminal.EnqueuePromptEvent(PromptInputResult.ToggleSession());
        var host = new TuiHost(terminal, store, new FakeQueryEngine(), new ConsoleTuiRenderer(new ConsoleTranscriptRenderer()), new FakePtyManager(), new FakeTaskManager());

        var result = await host.RunAsync(new ReplLaunchOptions(), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(terminal.RenderedFrames, frame => frame.Overlay is not null && frame.Overlay.Contains("Keyboard shortcuts", StringComparison.Ordinal));
        Assert.Contains(terminal.RenderedFrames, frame => frame.DrawerPane is not null && frame.DrawerPane.Contains("Sessions", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Tui_tasks_overlay_can_show_task_details()
    {
        var store = new JsonSessionStore(_dataRoot);
        var terminal = new FakeTerminalSession(["/tasks", "/tasks task-1", "exit"]);
        var taskManager = new FakeTaskManager();
        var task = new TaskRecord(
            "task-1",
            TaskKind.Worker,
            "Inspect repo",
            "Scan files",
            "parent-1",
            "worker-1",
            "claude-sonnet-4-5",
            PermissionMode.Default,
            ClawdNet.Core.Models.TaskStatus.Completed,
            DateTimeOffset.UtcNow.AddMinutes(-2),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            null,
            null,
            0,
            null,
            "/tmp",
            "Task completed successfully.",
            new TaskResult(true, "Task completed successfully."),
            [new TaskEvent(ClawdNet.Core.Models.TaskStatus.Running, "Worker launched.", DateTimeOffset.UtcNow.AddMinutes(-1))],
            [],
            "assistant: worker finished",
            3,
            DateTimeOffset.UtcNow,
            null);
        taskManager.Publish(task, task.Events![0]);
        var childTask = task with
        {
            Id = "task-2",
            Title = "Child task",
            ParentTaskId = "task-1",
            RootTaskId = "task-1",
            Depth = 1,
            Status = ClawdNet.Core.Models.TaskStatus.Running,
            LastStatusMessage = "Child running"
        };
        taskManager.Publish(task with { ChildTaskIds = ["task-2"] }, task.Events![0]);
        taskManager.Publish(childTask, new TaskEvent(ClawdNet.Core.Models.TaskStatus.Running, "Child running", DateTimeOffset.UtcNow));
        var host = new TuiHost(terminal, store, new FakeQueryEngine(), new ConsoleTuiRenderer(new ConsoleTranscriptRenderer()), new FakePtyManager(), taskManager);

        var result = await host.RunAsync(new ReplLaunchOptions(), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(terminal.RenderedFrames, frame => frame.DrawerPane is not null && frame.DrawerPane.Contains("Tasks", StringComparison.Ordinal));
        Assert.Contains(terminal.RenderedFrames, frame => frame.DrawerPane is not null && frame.DrawerPane.Contains("assistant: worker finished", StringComparison.Ordinal));
        Assert.Contains(terminal.RenderedFrames, frame => frame.DrawerPane is not null && frame.DrawerPane.Contains("Child task", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Tui_pty_overlay_lists_sessions_and_can_focus_selected_session()
    {
        var store = new JsonSessionStore(_dataRoot);
        var terminal = new FakeTerminalSession(["/pty", "/pty pty-2", "exit"]);
        var ptyManager = new FakePtyManager();
        ptyManager.Publish(FakePtyManager.NewState("cat", Environment.CurrentDirectory, "first", true, null, false, "pty-1"));
        ptyManager.Publish(FakePtyManager.NewState("python3", Environment.CurrentDirectory, "second", true, null, false, "pty-2"));
        var host = new TuiHost(terminal, store, new FakeQueryEngine(), new ConsoleTuiRenderer(new ConsoleTranscriptRenderer()), ptyManager, new FakeTaskManager());

        var result = await host.RunAsync(new ReplLaunchOptions(), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(terminal.RenderedFrames, frame => frame.DrawerPane is not null && frame.DrawerPane.Contains("PTY sessions", StringComparison.Ordinal));
        Assert.Contains(terminal.RenderedFrames, frame => frame.DrawerPane is not null && frame.DrawerPane.Contains("pty-1", StringComparison.Ordinal));
        Assert.Contains(terminal.RenderedFrames, frame => frame.DrawerPane is not null && frame.DrawerPane.Contains("pty-2", StringComparison.Ordinal));
        Assert.Equal("pty-2", ptyManager.State.CurrentSessionId);
    }

    [Fact]
    public async Task Tui_rename_slash_command_updates_session_title()
    {
        var store = new JsonSessionStore(_dataRoot);
        var terminal = new FakeTerminalSession(["/rename New Title", "exit"]);
        var host = new TuiHost(terminal, store, new FakeQueryEngine(), new ConsoleTuiRenderer(new ConsoleTranscriptRenderer()), new FakePtyManager(), new FakeTaskManager());

        var result = await host.RunAsync(new ReplLaunchOptions(), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        var sessions = await store.ListAsync(CancellationToken.None);
        Assert.Single(sessions);
        Assert.Equal("New Title", sessions[0].Title);
    }

    [Fact]
    public async Task Tui_status_slash_command_shows_session_info()
    {
        var store = new JsonSessionStore(_dataRoot);
        var terminal = new FakeTerminalSession(["/status", "exit"]);
        var host = new TuiHost(terminal, store, new FakeQueryEngine(), new ConsoleTuiRenderer(new ConsoleTranscriptRenderer()), new FakePtyManager(), new FakeTaskManager());

        var result = await host.RunAsync(new ReplLaunchOptions(), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(terminal.RenderedFrames, frame => frame.Overlay is not null && frame.Overlay.Contains("Session Status", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Tui_context_slash_command_shows_context_info()
    {
        var store = new JsonSessionStore(_dataRoot);
        var terminal = new FakeTerminalSession(["/context", "exit"]);
        var host = new TuiHost(terminal, store, new FakeQueryEngine(), new ConsoleTuiRenderer(new ConsoleTranscriptRenderer()), new FakePtyManager(), new FakeTaskManager());

        var result = await host.RunAsync(new ReplLaunchOptions(), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(terminal.RenderedFrames, frame => frame.Overlay is not null && frame.Overlay.Contains("Session Context", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Tui_pre_fills_composer_from_initial_prompt()
    {
        var store = new JsonSessionStore(_dataRoot);
        var terminal = new FakeTerminalSession(["exit"]);
        var host = new TuiHost(terminal, store, new FakeQueryEngine(), new ConsoleTuiRenderer(new ConsoleTranscriptRenderer()), new FakePtyManager(), new FakeTaskManager());

        var result = await host.RunAsync(new ReplLaunchOptions(InitialPrompt: "Explain this project"), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(terminal.RenderedFrames, frame => frame.ComposerPane.Contains("Explain this project", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Tui_shows_session_drawer_on_startup_when_prior_sessions_exist()
    {
        var store = new JsonSessionStore(_dataRoot);
        // Create two prior sessions
        await store.CreateAsync("Old session 1", "claude-sonnet-4-5", CancellationToken.None);
        await store.CreateAsync("Old session 2", "claude-sonnet-4-5", CancellationToken.None);

        var terminal = new FakeTerminalSession(["exit"]);
        var host = new TuiHost(terminal, store, new FakeQueryEngine(), new ConsoleTuiRenderer(new ConsoleTranscriptRenderer()), new FakePtyManager(), new FakeTaskManager());

        var result = await host.RunAsync(new ReplLaunchOptions(), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        // Session drawer should be shown on startup when prior sessions exist
        Assert.Contains(terminal.RenderedFrames, frame => frame.DrawerPane is not null && frame.DrawerPane.Contains("Sessions", StringComparison.Ordinal));
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataRoot))
        {
            Directory.Delete(_dataRoot, recursive: true);
        }
    }
}
