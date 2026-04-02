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
        Assert.Contains(terminal.RenderedFrames, frame => frame.Overlay is not null && frame.Overlay.Contains("Session details", StringComparison.Ordinal));
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
            "/tmp",
            "Task completed successfully.",
            new TaskResult(true, "Task completed successfully."),
            [new TaskEvent(ClawdNet.Core.Models.TaskStatus.Running, "Worker launched.", DateTimeOffset.UtcNow.AddMinutes(-1))],
            "assistant: worker finished",
            3,
            DateTimeOffset.UtcNow,
            null);
        taskManager.Publish(task, task.Events![0]);
        var host = new TuiHost(terminal, store, new FakeQueryEngine(), new ConsoleTuiRenderer(new ConsoleTranscriptRenderer()), new FakePtyManager(), taskManager);

        var result = await host.RunAsync(new ReplLaunchOptions(), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(terminal.RenderedFrames, frame => frame.Overlay is not null && frame.Overlay.Contains("Recent tasks", StringComparison.Ordinal));
        Assert.Contains(terminal.RenderedFrames, frame => frame.Overlay is not null && frame.Overlay.Contains("assistant: worker finished", StringComparison.Ordinal));
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataRoot))
        {
            Directory.Delete(_dataRoot, recursive: true);
        }
    }
}
