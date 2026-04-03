using System.Text;

namespace ClawdNet.Core.Services;

/// <summary>
/// Loads CLAUDE.md memory files and rules from the legacy config layout.
/// Loading order matches the legacy TypeScript CLI:
/// 1. User memory (~/.claude/CLAUDE.md)
/// 2. User rules (~/.claude/rules/*.md)
/// 3. Project memory ({cwd}/CLAUDE.md)
/// 4. Project .claude memory ({cwd}/.claude/CLAUDE.md)
/// 5. Project rules ({cwd}/.claude/rules/*.md)
/// Content is concatenated with source-file markers for debugging.
/// Respects CLAUDE_CODE_DISABLE_AUTO_MEMORY=1 to skip all loading.
/// </summary>
public class MemoryFileLoader
{
    /// <summary>
    /// Loads all memory files and returns the concatenated content.
    /// Returns null if CLAUDE_CODE_DISABLE_AUTO_MEMORY is set or no files exist.
    /// </summary>
    public string? LoadMemory(string? cwd = null, IReadOnlyCollection<string>? additionalDirs = null)
    {
        // Check if auto-memory is disabled
        var disableAutoMemory = Environment.GetEnvironmentVariable("CLAUDE_CODE_DISABLE_AUTO_MEMORY");
        if (disableAutoMemory == "1")
        {
            return null;
        }

        cwd ??= Environment.CurrentDirectory;
        var sb = new StringBuilder();

        // 1. User memory
        LoadSingleFile(sb, LegacyConfigPaths.GetUserMemoryPath(), "user-memory");

        // 2. User rules
        LoadDirectoryFiles(sb, LegacyConfigPaths.GetUserRulesDirectory(), "user-rules");

        // 3. Project memory
        LoadSingleFile(sb, LegacyConfigPaths.GetProjectMemoryPath(cwd), "project-memory");

        // 4. Project .claude memory
        LoadSingleFile(sb, LegacyConfigPaths.GetProjectClaudeMemoryPath(cwd), "project-claude-memory");

        // 5. Project rules
        LoadDirectoryFiles(sb, LegacyConfigPaths.GetProjectRulesDirectory(cwd), "project-rules");

        // 6. Additional directories (--add-dir support)
        if (additionalDirs is not null)
        {
            foreach (var dir in additionalDirs)
            {
                var normalizedDir = Path.GetFullPath(dir);

                // CLAUDE.md from the added directory
                LoadSingleFile(sb, Path.Combine(normalizedDir, "CLAUDE.md"), $"add-dir-memory:{normalizedDir}");

                // .claude/CLAUDE.md from the added directory
                LoadSingleFile(sb, Path.Combine(normalizedDir, ".claude", "CLAUDE.md"), $"add-dir-claude-memory:{normalizedDir}");

                // .claude/rules/*.md from the added directory
                LoadDirectoryFiles(sb, Path.Combine(normalizedDir, ".claude", "rules"), $"add-dir-rules:{normalizedDir}");
            }
        }

        return sb.Length > 0 ? sb.ToString() : null;
    }

    private static void LoadSingleFile(StringBuilder sb, string path, string marker)
    {
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            var content = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(content))
            {
                return;
            }

            if (sb.Length > 0)
            {
                sb.AppendLine();
                sb.AppendLine("---");
            }

            sb.AppendLine($"<!-- {marker}: {path} -->");
            sb.AppendLine(content);
        }
        catch (IOException)
        {
            // Skip files that can't be read
        }
        catch (UnauthorizedAccessException)
        {
            // Skip files without permission
        }
    }

    private static void LoadDirectoryFiles(StringBuilder sb, string directoryPath, string marker)
    {
        if (!Directory.Exists(directoryPath))
        {
            return;
        }

        try
        {
            var mdFiles = Directory.GetFiles(directoryPath, "*.md")
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);

            foreach (var file in mdFiles)
            {
                LoadSingleFile(sb, file, $"{marker}:{Path.GetFileName(file)}");
            }
        }
        catch (IOException)
        {
            // Skip directories that can't be read
        }
        catch (UnauthorizedAccessException)
        {
            // Skip directories without permission
        }
    }
}
