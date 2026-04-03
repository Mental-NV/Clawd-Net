using System.Text.Json;

namespace ClawdNet.Core.Services;

/// <summary>
/// Reads legacy JSONL transcript files for session resume.
/// Legacy transcripts are stored as ~/.claude/projects/{projectDir}/{sessionId}.jsonl
/// with append-only JSON lines containing message and tool-call events.
/// </summary>
public class LegacyTranscriptReader
{
    /// <summary>
    /// Reads a legacy JSONL transcript and returns the parsed lines.
    /// Returns an empty list if the file doesn't exist or is unreadable.
    /// For files over 100MB, only the last 10000 lines are read (like legacy CLI).
    /// </summary>
    public IReadOnlyList<JsonElement> ReadTranscript(string sessionId, string? cwd = null)
    {
        cwd ??= Environment.CurrentDirectory;
        var transcriptPath = LegacyConfigPaths.GetLegacyTranscriptPath(sessionId, cwd);
        return ReadTranscriptFromFile(transcriptPath);
    }

    /// <summary>
    /// Reads a transcript from an explicit file path.
    /// </summary>
    public IReadOnlyList<JsonElement> ReadTranscriptFromFile(string path)
    {
        if (!File.Exists(path))
        {
            return Array.Empty<JsonElement>();
        }

        try
        {
            var fileInfo = new FileInfo(path);
            const long maxSizeForFullRead = 100 * 1024 * 1024; // 100MB

            if (fileInfo.Length > maxSizeForFullRead)
            {
                return ReadTail(path);
            }

            return ReadAllLines(path);
        }
        catch (IOException)
        {
            return Array.Empty<JsonElement>();
        }
        catch (UnauthorizedAccessException)
        {
            return Array.Empty<JsonElement>();
        }
    }

    private static List<JsonElement> ReadAllLines(string path)
    {
        var results = new List<JsonElement>();
        foreach (var line in File.ReadLines(path))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                continue;
            }

            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                results.Add(doc.RootElement.Clone());
            }
            catch (JsonException)
            {
                // Skip malformed lines
            }
        }

        return results;
    }

    private static List<JsonElement> ReadTail(string path)
    {
        const int maxLines = 10000;
        var results = new List<JsonElement>();

        // Read lines and keep only the last N
        var allLines = File.ReadAllLines(path);
        var startIndex = Math.Max(0, allLines.Length - maxLines);

        for (var i = startIndex; i < allLines.Length; i++)
        {
            var trimmed = allLines[i].Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                continue;
            }

            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                results.Add(doc.RootElement.Clone());
            }
            catch (JsonException)
            {
                // Skip malformed lines
            }
        }

        return results;
    }
}
