using ClawdNet.Core.Models;

namespace ClawdNet.Runtime.Editing;

internal static class EditDiffFormatter
{
    public static string Format(IReadOnlyList<PreparedFileEdit> files)
    {
        var sections = new List<string>();
        foreach (var file in files)
        {
            sections.Add(FormatFile(file));
        }

        return string.Join(Environment.NewLine + Environment.NewLine, sections);
    }

    private static string FormatFile(PreparedFileEdit file)
    {
        var oldLabel = file.Operation == EditOperation.Create ? "/dev/null" : file.Path;
        var newLabel = file.Operation == EditOperation.Delete ? "/dev/null" : file.Path;
        var oldLines = SplitLines(file.OriginalContent ?? string.Empty);
        var newLines = SplitLines(file.UpdatedContent ?? string.Empty);

        var prefix = 0;
        while (prefix < oldLines.Length && prefix < newLines.Length && oldLines[prefix] == newLines[prefix])
        {
            prefix++;
        }

        var suffix = 0;
        while (suffix < oldLines.Length - prefix &&
               suffix < newLines.Length - prefix &&
               oldLines[oldLines.Length - suffix - 1] == newLines[newLines.Length - suffix - 1])
        {
            suffix++;
        }

        var removed = oldLines.Skip(prefix).Take(oldLines.Length - prefix - suffix).ToArray();
        var added = newLines.Skip(prefix).Take(newLines.Length - prefix - suffix).ToArray();
        var lines = new List<string>
        {
            $"--- {oldLabel}",
            $"+++ {newLabel}",
            "@@"
        };

        lines.AddRange(removed.Select(line => $"-{line}"));
        lines.AddRange(added.Select(line => $"+{line}"));
        return string.Join(Environment.NewLine, lines);
    }

    private static string[] SplitLines(string content)
    {
        return content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
    }
}
