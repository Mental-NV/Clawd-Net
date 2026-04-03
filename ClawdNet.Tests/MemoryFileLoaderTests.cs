using ClawdNet.Core.Services;

namespace ClawdNet.Tests;

public sealed class MemoryFileLoaderTests : IDisposable
{
    private readonly string _testDir = Path.Combine(Path.GetTempPath(), "clawdnet-tests", Guid.NewGuid().ToString("N"));
    private readonly MemoryFileLoader _loader = new();

    public MemoryFileLoaderTests()
    {
        Directory.CreateDirectory(_testDir);
        Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", _testDir);
        Environment.SetEnvironmentVariable("CLAUDE_CODE_DISABLE_AUTO_MEMORY", null);
        LegacyConfigPaths.ResetCache();
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", null);
        Environment.SetEnvironmentVariable("CLAUDE_CODE_DISABLE_AUTO_MEMORY", null);
        LegacyConfigPaths.ResetCache();
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, true);
        }
    }

    [Fact]
    public void LoadMemory_returns_null_when_disabled()
    {
        Environment.SetEnvironmentVariable("CLAUDE_CODE_DISABLE_AUTO_MEMORY", "1");
        var result = _loader.LoadMemory(_testDir);
        Assert.Null(result);
    }

    [Fact]
    public void LoadMemory_returns_null_when_no_files_exist()
    {
        var result = _loader.LoadMemory(_testDir);
        Assert.Null(result);
    }

    [Fact]
    public void LoadMemory_loads_user_memory()
    {
        var userMemoryPath = LegacyConfigPaths.GetUserMemoryPath();
        Directory.CreateDirectory(Path.GetDirectoryName(userMemoryPath)!);
        File.WriteAllText(userMemoryPath, "# User Memory\nBe helpful.");

        var result = _loader.LoadMemory(_testDir);

        Assert.NotNull(result);
        Assert.Contains("User Memory", result);
        Assert.Contains("Be helpful.", result);
        Assert.Contains("user-memory", result);
    }

    [Fact]
    public void LoadMemory_loads_project_memory()
    {
        var projectMemoryPath = LegacyConfigPaths.GetProjectMemoryPath(_testDir);
        File.WriteAllText(projectMemoryPath, "# Project Memory\nThis is a test project.");

        var result = _loader.LoadMemory(_testDir);

        Assert.NotNull(result);
        Assert.Contains("Project Memory", result);
        Assert.Contains("project-memory", result);
    }

    [Fact]
    public void LoadMemory_loads_project_claude_memory()
    {
        var dir = Path.Combine(_testDir, ".claude");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "CLAUDE.md"), "# Claude Memory\nFrom .claude dir.");

        var result = _loader.LoadMemory(_testDir);

        Assert.NotNull(result);
        Assert.Contains("From .claude dir.", result);
        Assert.Contains("project-claude-memory", result);
    }

    [Fact]
    public void LoadMemory_loads_rules_files()
    {
        var rulesDir = LegacyConfigPaths.GetUserRulesDirectory();
        Directory.CreateDirectory(rulesDir);
        File.WriteAllText(Path.Combine(rulesDir, "rule1.md"), "# Rule 1\nAlways be concise.");

        try
        {
            var result = _loader.LoadMemory(_testDir);

            Assert.NotNull(result);
            Assert.Contains("Always be concise.", result);
            Assert.Contains("user-rules", result);
        }
        finally
        {
            // Cleanup rules dir
            if (Directory.Exists(rulesDir))
            {
                Directory.Delete(rulesDir, true);
            }
        }
    }

    [Fact]
    public void LoadMemory_loads_from_additional_directories()
    {
        var extraDir = Path.Combine(_testDir, "other-project");
        Directory.CreateDirectory(extraDir);
        File.WriteAllText(Path.Combine(extraDir, "CLAUDE.md"), "# Extra\nExtra project memory.");

        var result = _loader.LoadMemory(_testDir, [extraDir]);

        Assert.NotNull(result);
        Assert.Contains("Extra project memory.", result);
        Assert.Contains("add-dir-memory", result);
    }

    [Fact]
    public void LoadMemory_handles_missing_files_gracefully()
    {
        // No files created - should not throw
        var result = _loader.LoadMemory(_testDir);
        Assert.Null(result);
    }

    [Fact]
    public void LoadMemory_concatenates_multiple_files()
    {
        var userMemoryPath = LegacyConfigPaths.GetUserMemoryPath();
        Directory.CreateDirectory(Path.GetDirectoryName(userMemoryPath)!);
        File.WriteAllText(userMemoryPath, "# User\nUser content.");

        // Create a separate project directory for project memory
        var projectDir = Path.Combine(_testDir, "project");
        Directory.CreateDirectory(projectDir);
        var projectMemoryPath = LegacyConfigPaths.GetProjectMemoryPath(projectDir);
        File.WriteAllText(projectMemoryPath, "# Project\nProject content.");

        var result = _loader.LoadMemory(projectDir);

        Assert.NotNull(result);
        Assert.Contains("User content.", result);
        Assert.Contains("Project content.", result);
        // Should have a separator
        Assert.Contains("---", result);
    }
}
