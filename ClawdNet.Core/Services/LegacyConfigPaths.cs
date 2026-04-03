namespace ClawdNet.Core.Services;

/// <summary>
/// Resolves legacy TypeScript CLI configuration paths.
/// Mirrors the legacy CLAUDE_CONFIG_DIR and ~/.claude layout.
/// </summary>
public static class LegacyConfigPaths
{
    private static string? _cachedLegacyConfigDir;

    /// <summary>
    /// Returns the legacy config root directory.
    /// Resolved from CLAUDE_CONFIG_DIR env var, falling back to ~/.claude.
    /// Result is cached per process lifetime (keyed on env var value).
    /// </summary>
    public static string GetLegacyConfigDir()
    {
        var envValue = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");

        // Re-cache only if env var changed
        if (_cachedLegacyConfigDir is null || !envValue?.Equals(_cachedLegacyConfigDir, StringComparison.Ordinal) == true)
        {
            _cachedLegacyConfigDir = string.IsNullOrWhiteSpace(envValue)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude")
                : Path.GetFullPath(envValue);
        }

        return _cachedLegacyConfigDir;
    }

    /// <summary>
    /// Returns the path to the user-level settings file: ~/.claude/settings.json
    /// </summary>
    public static string GetUserSettingsPath()
    {
        return Path.Combine(GetLegacyConfigDir(), "settings.json");
    }

    /// <summary>
    /// Returns the path to the project-level settings file: {cwd}/.claude/settings.json
    /// </summary>
    public static string GetProjectSettingsPath(string cwd)
    {
        return Path.Combine(cwd, ".claude", "settings.json");
    }

    /// <summary>
    /// Returns the path to the local project settings file: {cwd}/.claude/settings.local.json
    /// </summary>
    public static string GetLocalSettingsPath(string cwd)
    {
        return Path.Combine(cwd, ".claude", "settings.local.json");
    }

    /// <summary>
    /// Returns the path to the user memory file: ~/.claude/CLAUDE.md
    /// </summary>
    public static string GetUserMemoryPath()
    {
        return Path.Combine(GetLegacyConfigDir(), "CLAUDE.md");
    }

    /// <summary>
    /// Returns the path to the user rules directory: ~/.claude/rules/
    /// </summary>
    public static string GetUserRulesDirectory()
    {
        return Path.Combine(GetLegacyConfigDir(), "rules");
    }

    /// <summary>
    /// Returns the path to the project memory file: {cwd}/CLAUDE.md
    /// </summary>
    public static string GetProjectMemoryPath(string cwd)
    {
        return Path.Combine(cwd, "CLAUDE.md");
    }

    /// <summary>
    /// Returns the path to the project .claude memory file: {cwd}/.claude/CLAUDE.md
    /// </summary>
    public static string GetProjectClaudeMemoryPath(string cwd)
    {
        return Path.Combine(cwd, ".claude", "CLAUDE.md");
    }

    /// <summary>
    /// Returns the path to the project rules directory: {cwd}/.claude/rules/
    /// </summary>
    public static string GetProjectRulesDirectory(string cwd)
    {
        return Path.Combine(cwd, ".claude", "rules");
    }

    /// <summary>
    /// Returns the path to the project MCP config: {cwd}/.mcp.json
    /// </summary>
    public static string GetProjectMcpConfigPath(string cwd)
    {
        return Path.Combine(cwd, ".mcp.json");
    }

    /// <summary>
    /// Returns the legacy projects directory: ~/.claude/projects
    /// </summary>
    public static string GetLegacyProjectsDir()
    {
        return Path.Combine(GetLegacyConfigDir(), "projects");
    }

    /// <summary>
    /// Returns the legacy transcript path for a session: ~/.claude/projects/{projectDir}/{sessionId}.jsonl
    /// </summary>
    public static string GetLegacyTranscriptPath(string sessionId, string cwd)
    {
        var projectDir = SanitizeProjectDir(cwd);
        return Path.Combine(GetLegacyProjectsDir(), projectDir, $"{sessionId}.jsonl");
    }

    /// <summary>
    /// Sanitizes a directory path for use as a project directory name.
    /// Mirrors the legacy path sanitization (hash-based for cross-platform safety).
    /// </summary>
    public static string SanitizeProjectDir(string cwd)
    {
        // Use a hash-based approach for cross-platform safety
        // Legacy CLI uses path sanitization; we use SHA256 hex for uniqueness
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var bytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(cwd));
        return Convert.ToHexString(bytes).Replace("-", "").ToLowerInvariant();
    }

    /// <summary>
    /// Resets the cached legacy config dir. Useful for testing.
    /// </summary>
    public static void ResetCache()
    {
        _cachedLegacyConfigDir = null;
    }
}
