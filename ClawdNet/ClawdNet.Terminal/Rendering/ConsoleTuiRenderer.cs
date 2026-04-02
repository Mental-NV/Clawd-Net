using System.Text;
using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Models;
using ClawdNet.Terminal.Abstractions;
using ClawdNet.Terminal.Models;

namespace ClawdNet.Terminal.Rendering;

public sealed class ConsoleTuiRenderer : ITuiRenderer
{
    private readonly ITranscriptRenderer _transcriptRenderer;

    public ConsoleTuiRenderer(ITranscriptRenderer transcriptRenderer)
    {
        _transcriptRenderer = transcriptRenderer;
    }

    public TerminalFrame Render(TuiState state)
    {
        var transcript = _transcriptRenderer.Render(state.VisibleTranscript);
        var context = RenderContext(state);
        var composer = RenderComposer(state);
        var footer = _transcriptRenderer.RenderFooter(
            state.Session,
            state.PermissionMode,
            state.Pty,
            state.TranscriptViewport.FollowLiveOutput,
            state.TranscriptViewport.HasBufferedLiveOutput,
            state.Error);
        var overlay = RenderOverlay(state.Overlay);
        var header = $"ClawdNet TUI | focus={state.Focus} | size={state.Layout.Width}x{state.Layout.Height}";
        return new TerminalFrame(header, transcript, context, composer, footer, overlay, state.ClearScreen, state.UseAlternateScreen);
    }

    private string RenderContext(TuiState state)
    {
        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(state.ActivityDetail))
        {
            builder.AppendLine($"activity={state.ActivityState} | {state.ActivityDetail}");
        }
        else
        {
            builder.AppendLine($"activity={state.ActivityState}");
        }

        var draft = _transcriptRenderer.RenderDraft(state.Draft);
        if (!string.IsNullOrWhiteSpace(draft))
        {
            builder.AppendLine();
            builder.AppendLine("live");
            builder.AppendLine(draft);
        }

        var pty = _transcriptRenderer.RenderPty(state.Pty);
        if (!string.IsNullOrWhiteSpace(pty))
        {
            builder.AppendLine();
            builder.AppendLine("pty");
            builder.AppendLine(pty);
        }
        else if (state.Pty?.Sessions.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("pty");
            foreach (var session in state.Pty.Sessions.Take(5))
            {
                builder.AppendLine($"{(session.IsCurrent ? "*" : "-")} {session.SessionId} | {(session.IsRunning ? "running" : "stopped")} | {session.Command}");
            }
        }

        if (state.RecentTasks.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("tasks");
            foreach (var task in state.RecentTasks.Take(5))
            {
                var marker = task.Status == ClawdNet.Core.Models.TaskStatus.Running ? "*" : "-";
                builder.AppendLine($"{marker} {task.Id} | {task.Status} | {task.Title}");
                if (!string.IsNullOrWhiteSpace(task.Result?.Summary ?? task.LastStatusMessage))
                {
                    builder.AppendLine($"  {task.Result?.Summary ?? task.LastStatusMessage}");
                }
                builder.AppendLine($"  updated={task.UpdatedAtUtc:HH:mm:ss} | workerMessages={task.WorkerMessageCount}");
            }
        }

        return builder.Length == 0 ? "(no context)" : builder.ToString().TrimEnd();
    }

    private static string RenderComposer(TuiState state)
    {
        var focus = state.Focus == TuiFocusTarget.Composer ? "*" : "-";
        var buffer = string.IsNullOrWhiteSpace(state.ComposerBuffer) ? "(empty)" : state.ComposerBuffer;
        return $"{focus} composer{Environment.NewLine}{buffer}";
    }

    private static string? RenderOverlay(TuiOverlayState? overlay)
    {
        if (overlay is null || overlay.Kind == TuiOverlayKind.None)
        {
            return null;
        }

        return $"[{overlay.Kind}] {overlay.Title}{Environment.NewLine}{overlay.Content}";
    }
}
