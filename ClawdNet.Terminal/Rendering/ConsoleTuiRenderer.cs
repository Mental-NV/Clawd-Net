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
        // If full-screen PTY mode is active, render only the PTY overlay
        if (state.PtyFullScreen is not null && state.Focus == TuiFocusTarget.PtyFullScreen)
        {
            return RenderPtyFullScreen(state.PtyFullScreen);
        }

        var transcript = _transcriptRenderer.Render(state.VisibleTranscript);
        var context = RenderContext(state);
        var composer = RenderComposer(state);
        var drawer = RenderDrawer(state.Drawer);
        var footer = _transcriptRenderer.RenderFooter(
            state.Session,
            state.PermissionMode,
            state.Pty,
            state.TranscriptViewport.FollowLiveOutput,
            state.TranscriptViewport.HasBufferedLiveOutput,
            state.Error);
        var overlay = RenderOverlay(state.Overlay);
        var header = $"ClawdNet TUI | focus={state.Focus} | size={state.Layout.Width}x{state.Layout.Height}";
        return new TerminalFrame(header, transcript, context, composer, footer, drawer, overlay, state.ClearScreen, state.UseAlternateScreen);
    }

    private TerminalFrame RenderPtyFullScreen(PtyFullScreenState ptyState)
    {
        // Full-screen PTY view: use overlay as header bar, transcript as PTY output
        var headerOverlay = $"[PTY FULL SCREEN] {ptyState.Command} | {ptyState.SessionId} | {(ptyState.IsRunning ? "running" : "exited")} | Press Esc to exit";
        var output = string.IsNullOrWhiteSpace(ptyState.RecentOutput)
            ? "(waiting for output...)"
            : ptyState.RecentOutput;
        var footer = ptyState.StatusLine;
        return new TerminalFrame(
            string.Empty,
            output,
            string.Empty,
            string.Empty,
            footer,
            null,
            headerOverlay,
            true,
            true);
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
                var progressSuffix = task.ProgressPercent.HasValue ? $" | progress={task.ProgressPercent}%" : "";
                var depSuffix = (task.DependsOnTaskIds?.Count ?? 0) > 0
                    ? $" | deps={string.Join(",", task.DependsOnTaskIds!.Take(2))}"
                    : "";
                builder.AppendLine($"{marker} {task.Id} | {task.Status} | depth={task.Depth} | children={task.ChildTaskIds?.Count ?? 0}{depSuffix} | {task.Title}{progressSuffix}");
                if (!string.IsNullOrWhiteSpace(task.Result?.Summary ?? task.LastStatusMessage))
                {
                    builder.AppendLine($"  {task.Result?.Summary ?? task.LastStatusMessage}");
                }
                if (!string.IsNullOrWhiteSpace(task.ProgressMessage))
                {
                    builder.AppendLine($"  progress: {task.ProgressMessage}");
                }
                builder.AppendLine($"  parent={(task.ParentTaskId ?? "root")} | updated={task.UpdatedAtUtc:HH:mm:ss} | workerMessages={task.WorkerMessageCount}");
            }
        }

        if (state.ActivityFeed.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("activity-feed");
            foreach (var line in state.ActivityFeed.Take(5))
            {
                builder.AppendLine(line);
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

    private static string? RenderDrawer(TuiDrawerState? drawer)
    {
        if (drawer is null || drawer.Kind == TuiDrawerKind.None)
        {
            return null;
        }

        var builder = new StringBuilder();
        builder.AppendLine($"[{drawer.Kind}] {drawer.Title}");
        if (drawer.Items.Count == 0)
        {
            builder.AppendLine("(empty)");
        }
        else
        {
            foreach (var item in drawer.Items)
            {
                var marker = item.IsSelected ? ">" : item.IsActive ? "*" : "-";
                builder.AppendLine($"{marker} {item.Title}");
                if (!string.IsNullOrWhiteSpace(item.Subtitle))
                {
                    builder.AppendLine($"  {item.Subtitle}");
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(drawer.DetailTitle))
        {
            builder.AppendLine();
            builder.AppendLine(drawer.DetailTitle);
            foreach (var line in drawer.DetailLines ?? [])
            {
                builder.AppendLine(line);
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static string? RenderOverlay(TuiOverlayState? overlay)
    {
        if (overlay is null || overlay.Kind == TuiOverlayKind.None)
        {
            return null;
        }

        var builder = new StringBuilder();
        builder.AppendLine($"[{overlay.Kind}] {overlay.Title}");
        if (!string.IsNullOrWhiteSpace(overlay.Summary))
        {
            builder.AppendLine(overlay.Summary);
        }

        foreach (var section in overlay.Sections ?? [])
        {
            builder.AppendLine();
            builder.AppendLine(section.Title);
            foreach (var line in section.Lines)
            {
                builder.AppendLine(line);
            }
        }

        if (overlay.RequiresConfirmation)
        {
            builder.AppendLine();
            builder.AppendLine($"actions: {overlay.PrimaryActionLabel ?? "confirm"} / {overlay.SecondaryActionLabel ?? "cancel"}");
        }

        return builder.ToString().TrimEnd();
    }
}
