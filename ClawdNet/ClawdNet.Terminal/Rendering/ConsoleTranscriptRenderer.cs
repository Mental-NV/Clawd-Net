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

    public string RenderStatus(ConversationSession session, string? error = null)
    {
        var status = $"[session {session.Id}] [model {session.Model}] [messages {session.Messages.Count}]";
        return string.IsNullOrWhiteSpace(error)
            ? status
            : $"{status} [error {error}]";
    }

    private static string RenderEntry(ConversationMessage entry)
    {
        var timestamp = $"[{entry.TimestampUtc:O}] ";

        return entry.Role switch
        {
            "user" => $"{timestamp}You: {entry.Content}",
            "assistant" => $"{timestamp}ClawdNet: {entry.Content}",
            "tool_use" => $"{timestamp}[tool use: {entry.ToolName}] {entry.Content}",
            "tool_result" when entry.IsError => $"{timestamp}[tool error: {entry.ToolName}] {entry.Content}",
            "tool_result" => $"{timestamp}[tool result: {entry.ToolName}] {entry.Content}",
            _ => $"{timestamp}{entry.Role}: {entry.Content}"
        };
    }
}
