using System.Text.Json;
using ClawdNet.Core.Services;

namespace ClawdNet.Tests;

public sealed class LegacySettingsLoaderTests : IDisposable
{
    private readonly string _testDir = Path.Combine(Path.GetTempPath(), "clawdnet-tests", Guid.NewGuid().ToString("N"));
    private readonly LegacySettingsLoader _loader = new();

    public LegacySettingsLoaderTests()
    {
        Directory.CreateDirectory(_testDir);
        // Set up a fake CLAUDE_CONFIG_DIR
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
    public void LoadMergedSettings_returns_empty_when_no_files_exist()
    {
        var result = _loader.LoadMergedSettings(_testDir);
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void LoadMergedSettings_loads_user_settings()
    {
        var userSettingsPath = LegacyConfigPaths.GetUserSettingsPath();
        Directory.CreateDirectory(Path.GetDirectoryName(userSettingsPath)!);
        File.WriteAllText(userSettingsPath, "{\"allowedTools\": [\"echo\", \"grep\"]}");

        var result = _loader.LoadMergedSettings(_testDir);

        Assert.True(result.ContainsKey("allowedTools"));
        Assert.Equal("[\"echo\",\"grep\"]", JsonSerializer.Serialize(result["allowedTools"]));
    }

    [Fact]
    public void LoadMergedSettings_project_overrides_user()
    {
        var userSettingsPath = LegacyConfigPaths.GetUserSettingsPath();
        Directory.CreateDirectory(Path.GetDirectoryName(userSettingsPath)!);
        File.WriteAllText(userSettingsPath, "{\"model\": \"claude-3-5-sonnet\"}");

        var projectDir = Path.Combine(_testDir, "project");
        Directory.CreateDirectory(Path.Combine(projectDir, ".claude"));
        File.WriteAllText(
            Path.Combine(projectDir, ".claude", "settings.json"),
            "{\"model\": \"claude-opus\"}");

        var result = _loader.LoadMergedSettings(projectDir);

        Assert.Equal("claude-opus", result["model"]);
    }

    [Fact]
    public void LoadMergedSettings_local_overrides_project()
    {
        var projectDir = Path.Combine(_testDir, "project");
        Directory.CreateDirectory(Path.Combine(projectDir, ".claude"));

        File.WriteAllText(
            Path.Combine(projectDir, ".claude", "settings.json"),
            "{\"model\": \"claude-3-5-sonnet\"}");
        File.WriteAllText(
            Path.Combine(projectDir, ".claude", "settings.local.json"),
            "{\"model\": \"claude-opus\"}");

        var result = _loader.LoadMergedSettings(projectDir);

        Assert.Equal("claude-opus", result["model"]);
    }

    [Fact]
    public void LoadMergedSettings_merges_arrays()
    {
        var userSettingsPath = LegacyConfigPaths.GetUserSettingsPath();
        Directory.CreateDirectory(Path.GetDirectoryName(userSettingsPath)!);
        File.WriteAllText(userSettingsPath, "{\"allowedTools\": [\"echo\"]}");

        var projectDir = Path.Combine(_testDir, "project");
        Directory.CreateDirectory(Path.Combine(projectDir, ".claude"));
        File.WriteAllText(
            Path.Combine(projectDir, ".claude", "settings.json"),
            "{\"allowedTools\": [\"grep\"]}");

        var result = _loader.LoadMergedSettings(projectDir);

        var tools = result["allowedTools"] as List<object?>;
        Assert.NotNull(tools);
        Assert.Contains("echo", tools);
        Assert.Contains("grep", tools);
    }

    [Fact]
    public void LoadMergedSettings_skips_invalid_json()
    {
        var userSettingsPath = LegacyConfigPaths.GetUserSettingsPath();
        Directory.CreateDirectory(Path.GetDirectoryName(userSettingsPath)!);
        File.WriteAllText(userSettingsPath, "{invalid json}");

        var result = _loader.LoadMergedSettings(_testDir);

        Assert.Empty(result);
    }

    [Fact]
    public void LoadSettingsFromDirectory_loads_from_claude_subdirectory()
    {
        var extraDir = Path.Combine(_testDir, "extra-project");
        Directory.CreateDirectory(Path.Combine(extraDir, ".claude"));
        File.WriteAllText(
            Path.Combine(extraDir, ".claude", "settings.json"),
            "{\"allowedTools\": [\"file_read\"]}");

        var result = _loader.LoadSettingsFromDirectory(extraDir);

        Assert.True(result.ContainsKey("allowedTools"));
    }

    [Fact]
    public void LoadSettingsFromDirectory_local_overrides_base()
    {
        var extraDir = Path.Combine(_testDir, "extra-project");
        Directory.CreateDirectory(Path.Combine(extraDir, ".claude"));
        File.WriteAllText(
            Path.Combine(extraDir, ".claude", "settings.json"),
            "{\"model\": \"claude-3-5-sonnet\"}");
        File.WriteAllText(
            Path.Combine(extraDir, ".claude", "settings.local.json"),
            "{\"model\": \"claude-opus\"}");

        var result = _loader.LoadSettingsFromDirectory(extraDir);

        Assert.Equal("claude-opus", result["model"]);
    }

    [Fact]
    public void LoadMergedSettings_handles_empty_file()
    {
        var userSettingsPath = LegacyConfigPaths.GetUserSettingsPath();
        Directory.CreateDirectory(Path.GetDirectoryName(userSettingsPath)!);
        File.WriteAllText(userSettingsPath, "");

        var result = _loader.LoadMergedSettings(_testDir);

        Assert.Empty(result);
    }
}
