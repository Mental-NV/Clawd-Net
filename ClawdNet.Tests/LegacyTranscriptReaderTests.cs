using System.Text.Json;
using ClawdNet.Core.Services;

namespace ClawdNet.Tests;

public sealed class LegacyTranscriptReaderTests : IDisposable
{
    private readonly string _testDir = Path.Combine(Path.GetTempPath(), "clawdnet-tests", Guid.NewGuid().ToString("N"));
    private readonly LegacyTranscriptReader _reader = new();

    public LegacyTranscriptReaderTests()
    {
        Directory.CreateDirectory(_testDir);
        // Ensure legacy config paths point to our test directory
        Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", _testDir);
        LegacyConfigPaths.ResetCache();
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", null);
        LegacyConfigPaths.ResetCache();
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, true);
        }
    }

    [Fact]
    public void ReadTranscript_returns_empty_for_missing_file()
    {
        var result = _reader.ReadTranscript("nonexistent-session", _testDir);
        Assert.Empty(result);
    }

    [Fact]
    public void ReadTranscriptFromFile_returns_empty_for_missing_file()
    {
        var result = _reader.ReadTranscriptFromFile(Path.Combine(_testDir, "nonexistent.jsonl"));
        Assert.Empty(result);
    }

    [Fact]
    public void ReadTranscriptFromFile_parses_jsonl_lines()
    {
        var lines = new[]
        {
            "{\"type\": \"user\", \"content\": \"Hello\"}",
            "{\"type\": \"assistant\", \"content\": \"Hi there\"}",
            "{\"type\": \"tool\", \"name\": \"echo\", \"args\": {\"text\": \"test\"}}"
        };
        var path = Path.Combine(_testDir, "test.jsonl");
        File.WriteAllLines(path, lines);

        var result = _reader.ReadTranscriptFromFile(path);

        Assert.Equal(3, result.Count);
        Assert.Equal("user", result[0].GetProperty("type").GetString());
        Assert.Equal("assistant", result[1].GetProperty("type").GetString());
        Assert.Equal("tool", result[2].GetProperty("type").GetString());
    }

    [Fact]
    public void ReadTranscriptFromFile_skips_empty_lines()
    {
        var lines = new[]
        {
            "{\"type\": \"user\"}",
            "",
            "  ",
            "{\"type\": \"assistant\"}"
        };
        var path = Path.Combine(_testDir, "test.jsonl");
        File.WriteAllLines(path, lines);

        var result = _reader.ReadTranscriptFromFile(path);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void ReadTranscriptFromFile_skips_malformed_json()
    {
        var lines = new[]
        {
            "{\"type\": \"user\"}",
            "{bad json}",
            "{\"type\": \"assistant\"}"
        };
        var path = Path.Combine(_testDir, "test.jsonl");
        File.WriteAllLines(path, lines);

        var result = _reader.ReadTranscriptFromFile(path);

        Assert.Equal(2, result.Count);
        Assert.Equal("user", result[0].GetProperty("type").GetString());
        Assert.Equal("assistant", result[1].GetProperty("type").GetString());
    }

    [Fact]
    public void ReadTranscriptFromFile_resolves_session_path()
    {
        // Create the legacy transcript path manually
        var sessionId = "test-session-123";
        var sanitizedCwd = LegacyConfigPaths.SanitizeProjectDir(_testDir);
        var transcriptDir = Path.Combine(LegacyConfigPaths.GetLegacyProjectsDir(), sanitizedCwd);
        Directory.CreateDirectory(transcriptDir);
        var transcriptPath = Path.Combine(transcriptDir, $"{sessionId}.jsonl");

        File.WriteAllText(transcriptPath, "{\"type\": \"user\", \"content\": \"test\"}");

        try
        {
            var result = _reader.ReadTranscript(sessionId, _testDir);
            Assert.Single(result);
            Assert.Equal("user", result[0].GetProperty("type").GetString());
        }
        finally
        {
            // Cleanup only the test transcript directory
            if (Directory.Exists(transcriptDir))
            {
                Directory.Delete(transcriptDir, true);
            }
        }
    }

    [Fact]
    public void ReadTranscriptFromFile_handles_empty_file()
    {
        var path = Path.Combine(_testDir, "empty.jsonl");
        File.WriteAllText(path, "");

        var result = _reader.ReadTranscriptFromFile(path);

        Assert.Empty(result);
    }

    [Fact]
    public void ReadTranscriptFromFile_clones_json_elements()
    {
        var path = Path.Combine(_testDir, "test.jsonl");
        File.WriteAllText(path, "{\"type\": \"user\", \"nested\": {\"key\": \"value\"}}");

        var result = _reader.ReadTranscriptFromFile(path);

        // Should not throw - elements are cloned and usable after document disposal
        Assert.Single(result);
        Assert.Equal("user", result[0].GetProperty("type").GetString());
        Assert.Equal("value", result[0].GetProperty("nested").GetProperty("key").GetString());
    }
}
