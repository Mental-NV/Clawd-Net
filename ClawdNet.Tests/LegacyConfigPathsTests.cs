using ClawdNet.Core.Services;

namespace ClawdNet.Tests;

public sealed class LegacyConfigPathsTests : IDisposable
{
    private readonly string _testDir = Path.Combine(Path.GetTempPath(), "clawdnet-tests", Guid.NewGuid().ToString("N"));

    public LegacyConfigPathsTests()
    {
        Directory.CreateDirectory(_testDir);
        LegacyConfigPaths.ResetCache();
    }

    public void Dispose()
    {
        LegacyConfigPaths.ResetCache();
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, true);
        }
    }

    [Fact]
    public void GetLegacyConfigDir_returns_env_var_when_set()
    {
        Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", _testDir);
        try
        {
            var result = LegacyConfigPaths.GetLegacyConfigDir();
            Assert.Equal(_testDir, result);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", null);
        }
    }

    [Fact]
    public void GetLegacyConfigDir_returns_home_claude_when_env_not_set()
    {
        Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", null);
        LegacyConfigPaths.ResetCache();

        var result = LegacyConfigPaths.GetLegacyConfigDir();
        var expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
        Assert.Equal(expected, result);
    }

    [Fact]
    public void GetUserSettingsPath_returns_correct_path()
    {
        Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", _testDir);
        try
        {
            LegacyConfigPaths.ResetCache();
            var result = LegacyConfigPaths.GetUserSettingsPath();
            Assert.Equal(Path.Combine(_testDir, "settings.json"), result);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", null);
        }
    }

    [Fact]
    public void GetProjectSettingsPath_returns_correct_path()
    {
        var result = LegacyConfigPaths.GetProjectSettingsPath(_testDir);
        Assert.Equal(Path.Combine(_testDir, ".claude", "settings.json"), result);
    }

    [Fact]
    public void GetLocalSettingsPath_returns_correct_path()
    {
        var result = LegacyConfigPaths.GetLocalSettingsPath(_testDir);
        Assert.Equal(Path.Combine(_testDir, ".claude", "settings.local.json"), result);
    }

    [Fact]
    public void GetUserMemoryPath_returns_correct_path()
    {
        Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", _testDir);
        try
        {
            LegacyConfigPaths.ResetCache();
            var result = LegacyConfigPaths.GetUserMemoryPath();
            Assert.Equal(Path.Combine(_testDir, "CLAUDE.md"), result);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", null);
        }
    }

    [Fact]
    public void GetProjectMemoryPath_returns_correct_path()
    {
        var result = LegacyConfigPaths.GetProjectMemoryPath(_testDir);
        Assert.Equal(Path.Combine(_testDir, "CLAUDE.md"), result);
    }

    [Fact]
    public void GetProjectClaudeMemoryPath_returns_correct_path()
    {
        var result = LegacyConfigPaths.GetProjectClaudeMemoryPath(_testDir);
        Assert.Equal(Path.Combine(_testDir, ".claude", "CLAUDE.md"), result);
    }

    [Fact]
    public void GetProjectMcpConfigPath_returns_correct_path()
    {
        var result = LegacyConfigPaths.GetProjectMcpConfigPath(_testDir);
        Assert.Equal(Path.Combine(_testDir, ".mcp.json"), result);
    }

    [Fact]
    public void SanitizeProjectDir_produces_consistent_hash()
    {
        var cwd = "/home/user/my-project";
        var result1 = LegacyConfigPaths.SanitizeProjectDir(cwd);
        var result2 = LegacyConfigPaths.SanitizeProjectDir(cwd);
        Assert.Equal(result1, result2);
        Assert.False(string.IsNullOrWhiteSpace(result1));
    }

    [Fact]
    public void SanitizeProjectDir_produces_different_hashes_for_different_paths()
    {
        var result1 = LegacyConfigPaths.SanitizeProjectDir("/home/user/project-a");
        var result2 = LegacyConfigPaths.SanitizeProjectDir("/home/user/project-b");
        Assert.NotEqual(result1, result2);
    }
}
