using ClawdNet.Core.Models;
using ClawdNet.Terminal.Rendering;

namespace ClawdNet.Tests;

public sealed class ConsoleTranscriptRendererTests
{
    [Fact]
    public void Render_formats_timestamp_role_and_content()
    {
        var renderer = new ConsoleTranscriptRenderer();
        var transcript = new[]
        {
            new ConversationMessage("system", "ready", new DateTimeOffset(2026, 4, 1, 12, 0, 0, TimeSpan.Zero))
        };

        var rendered = renderer.Render(transcript);

        Assert.Equal("[12:00:00] system: ready", rendered);
    }

    [Fact]
    public void Render_footer_includes_model_permission_and_message_count()
    {
        var renderer = new ConsoleTranscriptRenderer();
        var session = new ConversationSession(
            "session-1",
            "Interactive session",
            "claude-sonnet-4-5",
            new DateTimeOffset(2026, 4, 1, 12, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 4, 1, 12, 0, 0, TimeSpan.Zero),
            [new ConversationMessage("assistant", "ready", DateTimeOffset.UtcNow)]);

        var footer = renderer.RenderFooter(session, PermissionMode.AcceptEdits);

        Assert.Contains("session=session-1", footer);
        Assert.Contains("permission=accept-edits", footer);
        Assert.Contains("messages=1", footer);
    }

    [Fact]
    public void Render_activity_formats_help_state()
    {
        var renderer = new ConsoleTranscriptRenderer();

        var activity = renderer.RenderActivity(TerminalActivityState.ShowingHelp, "Commands: /help");

        Assert.Equal("Commands: /help", activity);
    }

    [Fact]
    public void Render_formats_edit_preview_entries()
    {
        var renderer = new ConsoleTranscriptRenderer();
        var transcript = new[]
        {
            new ConversationMessage("edit_preview", "Edit batch touches 1 file(s).", new DateTimeOffset(2026, 4, 1, 12, 0, 0, TimeSpan.Zero), "apply_patch")
        };

        var rendered = renderer.Render(transcript);

        Assert.Equal("[12:00:00] Preview  apply_patch -> Edit batch touches 1 file(s).", rendered);
    }
}
