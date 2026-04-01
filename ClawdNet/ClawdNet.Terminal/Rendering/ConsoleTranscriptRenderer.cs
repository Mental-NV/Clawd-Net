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

    public string RenderFooter(
        ConversationSession session,
        PermissionMode permissionMode,
        string? error = null)
    {
        var status = $"session={session.Id} | model={session.Model} | permission={FormatPermissionMode(permissionMode)} | messages={session.Messages.Count}";
        return string.IsNullOrWhiteSpace(error)
            ? status
            : $"{status} | error={error}";
    }

    public string? RenderActivity(TerminalActivityState state, string? detail = null)
    {
        return state switch
        {
            TerminalActivityState.Idle => null,
            TerminalActivityState.Ready => "Ready for input.",
            TerminalActivityState.WaitingForModel => detail ?? "Waiting for model response...",
            TerminalActivityState.AwaitingApproval => detail ?? "Awaiting approval...",
            TerminalActivityState.ShowingHelp => detail ?? "Available commands: /help, /session, /clear, /exit",
            TerminalActivityState.ShowingSession => detail,
            TerminalActivityState.Cleared => detail ?? "Screen cleared. Session history is preserved.",
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
