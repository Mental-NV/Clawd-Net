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
            builder
                .Append('[')
                .Append(entry.TimestampUtc.ToString("O"))
                .Append("] ")
                .Append(entry.Role)
                .Append(entry.ToolName is null ? string.Empty : $"({entry.ToolName})")
                .Append(": ")
                .AppendLine(entry.Content);
        }

        return builder.ToString().TrimEnd();
    }
}
