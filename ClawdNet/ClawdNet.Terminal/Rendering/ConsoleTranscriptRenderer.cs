using System.Text;
using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Models;

namespace ClawdNet.Terminal.Rendering;

public sealed class ConsoleTranscriptRenderer : ITranscriptRenderer
{
    public string Render(IReadOnlyList<ConversationMessage> entries)
    {
        if (entries.Count == 0)
        {
            return "Transcript is empty.";
        }

        var builder = new StringBuilder();
        foreach (var entry in entries)
        {
            builder.AppendLine(RenderEntry(entry));
        }

        return builder.ToString().TrimEnd();
    }

    public string? RenderDraft(StreamingAssistantDraft? draft)
    {
        if (draft is null || !draft.IsActive)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(draft.ToolName))
        {
            return string.IsNullOrWhiteSpace(draft.Detail)
                ? $"[live] tool={draft.ToolName}"
                : $"[live] tool={draft.ToolName}{Environment.NewLine}{draft.Detail}".TrimEnd();
        }

        if (!string.IsNullOrWhiteSpace(draft.Text))
        {
            return $"[live] ClawdNet {draft.Text}";
        }

        return string.IsNullOrWhiteSpace(draft.Detail)
            ? "[live] ClawdNet is thinking..."
            : $"[live] {draft.Detail}";
    }

    public string? RenderPty(PtySessionState? state)
    {
        if (state is null)
        {
            return null;
        }

        var header = $"[pty] {state.Command} | running={state.IsRunning} | exitCode={(state.ExitCode.HasValue ? state.ExitCode.Value.ToString() : "n/a")}";
        var output = string.IsNullOrWhiteSpace(state.RecentOutput) ? "(no output yet)" : state.RecentOutput.TrimEnd();
        var clipped = state.IsOutputClipped ? $"{Environment.NewLine}[pty] output clipped to recent buffer" : string.Empty;
        return $"{header}{Environment.NewLine}{output}{clipped}".TrimEnd();
    }

    public string RenderFooter(
        ConversationSession session,
        PermissionMode permissionMode,
        PtySessionState? ptyState = null,
        bool followLiveOutput = true,
        bool hasBufferedLiveOutput = false,
        string? error = null)
    {
        var ptyStatus = ptyState is null
            ? "pty=idle"
            : $"pty={(ptyState.IsRunning ? "running" : "stopped")}";
        var followStatus = followLiveOutput
            ? "follow=live"
            : hasBufferedLiveOutput ? "follow=paused*" : "follow=paused";
        var status = $"session={session.Id} | model={session.Model} | permission={FormatPermissionMode(permissionMode)} | messages={session.Messages.Count} | {ptyStatus} | {followStatus}";
        if (!string.IsNullOrWhiteSpace(error))
        {
            status = $"{status} | error={error}";
        }

        var hints = "keys=Up/Down history | PgUp/PgDn scroll | End bottom";
        return $"{status}{Environment.NewLine}{hints}";
    }

    public string? RenderActivity(TerminalActivityState state, string? detail = null)
    {
        return state switch
        {
            TerminalActivityState.Idle => null,
            TerminalActivityState.Ready => detail ?? "Ready for input.",
            TerminalActivityState.WaitingForModel => detail ?? "Waiting for model response...",
            TerminalActivityState.StreamingResponse => detail ?? "Streaming assistant response...",
            TerminalActivityState.RunningTool => detail ?? "Running tool...",
            TerminalActivityState.ReviewingEdits => detail ?? "Reviewing edit batch...",
            TerminalActivityState.AwaitingApproval => detail ?? "Awaiting approval...",
            TerminalActivityState.ShowingHelp => detail ?? "Available commands: /help, /session, /tasks, /pty, /clear, /bottom, /exit",
            TerminalActivityState.ShowingSession => detail,
            TerminalActivityState.Cleared => detail ?? "Screen cleared. Session history is preserved.",
            TerminalActivityState.Interrupted => detail ?? "Interrupted active turn.",
            TerminalActivityState.Error => detail,
            TerminalActivityState.Exiting => detail ?? "Exiting ClawdNet.",
            _ => detail
        };
    }

    private static string RenderEntry(ConversationMessage entry)
    {
        var timestamp = $"[{entry.TimestampUtc:HH:mm:ss}] ";

        return entry.Role switch
        {
            "user" => $"{timestamp}You      {entry.Content}",
            "assistant" => $"{timestamp}ClawdNet {entry.Content}",
            "tool_use" => $"{timestamp}Tool     {entry.ToolName} -> {entry.Content}",
            "permission" when entry.IsError => $"{timestamp}Deny     {entry.ToolName} -> {entry.Content}",
            "permission" => $"{timestamp}Approve  {entry.ToolName} -> {entry.Content}",
            "edit_preview" => $"{timestamp}Preview  {entry.ToolName} -> {entry.Content}",
            "edit_approved" => $"{timestamp}Apply    {entry.ToolName} -> {entry.Content}",
            "edit_rejected" => $"{timestamp}Reject   {entry.ToolName} -> {entry.Content}",
            "task_started" => $"{timestamp}Task     {entry.ToolName} -> {entry.Content}",
            "task_updated" => $"{timestamp}Task     {entry.ToolName} -> {entry.Content}",
            "task_completed" => $"{timestamp}Task     {entry.ToolName} -> {entry.Content}",
            "task_failed" => $"{timestamp}Task     {entry.ToolName} -> {entry.Content}",
            "task_canceled" => $"{timestamp}Task     {entry.ToolName} -> {entry.Content}",
            "tool_result" when entry.IsError => $"{timestamp}Error    {entry.ToolName} -> {entry.Content}",
            "tool_result" => $"{timestamp}Result   {entry.ToolName} -> {entry.Content}",
            _ => $"{timestamp}{entry.Role}: {entry.Content}"
        };
    }

    private static string FormatPermissionMode(PermissionMode permissionMode)
    {
        return permissionMode switch
        {
            PermissionMode.Default => "default",
            PermissionMode.AcceptEdits => "accept-edits",
            PermissionMode.BypassPermissions => "bypass-permissions",
            _ => permissionMode.ToString()
        };
    }
}
