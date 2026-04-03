using ClawdNet.Core.Models;
using ClawdNet.Terminal.Models;
using ClawdNet.Terminal.Rendering;

namespace ClawdNet.Tests;

public sealed class ConsoleTuiRendererTests
{
    [Fact]
    public void Render_builds_frame_with_transcript_context_composer_and_overlay()
    {
        var session = new ConversationSession(
            "session-1",
            "TUI session",
            "claude-sonnet-4-5",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            [new ConversationMessage("assistant", "ready", new DateTimeOffset(2026, 4, 2, 12, 0, 0, TimeSpan.Zero))]);
        var renderer = new ConsoleTuiRenderer(new ConsoleTranscriptRenderer());

        var frame = renderer.Render(new TuiState(
            session,
            PermissionMode.Default,
            session.Messages,
            [],
            [],
            "hello",
            TuiFocusTarget.Composer,
            new TuiDrawerState(
                TuiDrawerKind.Tasks,
                "Tasks",
                [new TuiDrawerItem("task-1", "Index repo", "running", true, true)],
                "Task detail: task-1",
                ["status=running"]),
            new TuiOverlayState(
                TuiOverlayKind.Help,
                "Keyboard shortcuts",
                "Tab: focus",
                [new TuiOverlaySection("Keys", ["Tab: focus"])]),
            new TuiLayoutState(120, 40, 88, 32),
            new TerminalViewportState(),
            new TerminalViewportState(),
            null,
            null,
            TerminalActivityState.Ready,
            "Ready",
            null,
            true,
            true));

        Assert.Contains("ClawdNet TUI", frame.Header);
        Assert.Contains("ready", frame.TranscriptPane);
        Assert.Contains("composer", frame.ComposerPane, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Tasks", frame.DrawerPane);
        Assert.Contains("Keyboard shortcuts", frame.Overlay);
    }
}
