using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Models;

namespace ClawdNet.Core.Commands;

/// <summary>
/// Diagnostic command that shows system health, configuration status, and provider connectivity.
/// </summary>
public sealed class DoctorCommandHandler : ICommandHandler
{
    public string Name => "doctor";

    public string HelpSummary => "Show system health and diagnostic information";

    public string HelpText => """
Usage: clawdnet doctor

Show system health, configuration status, and provider connectivity information.

This command checks:
  - Application version and runtime info
  - Configuration file presence
  - Provider configuration and API key status
  - Session store status
  - Plugin, MCP, and LSP server status

Examples:
  clawdnet doctor
""";

    public bool CanHandle(CommandRequest request)
    {
        return request.Arguments.Count > 0
            && string.Equals(request.Arguments[0], "doctor", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<CommandExecutionResult> ExecuteAsync(
        CommandContext context,
        CommandRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var providerStatus = await GetProviderStatusSafe(context, cancellationToken);
            var sessionStatus = await GetSessionStatusSafe(context, cancellationToken);
            
            var lines = new List<string>
            {
                "ClawdNet Doctor - System Diagnostics",
                "====================================",
                string.Empty,
                "Application:",
                $"  Version:    {context.Version}",
                $"  Runtime:    {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}",
                $"  OS:         {System.Runtime.InteropServices.RuntimeInformation.OSDescription}",
                $"  Arch:       {System.Runtime.InteropServices.RuntimeInformation.OSArchitecture}",
                string.Empty,
                FormatConfigStatus(context),
                providerStatus,
                sessionStatus,
                FormatPluginStatus(context),
                FormatMcpStatus(context),
                FormatLspStatus(context),
                string.Empty,
                "Run 'clawdnet --help' for available commands."
            };

            return CommandExecutionResult.Success(string.Join(Environment.NewLine, lines));
        }
        catch (Exception ex)
        {
            return CommandExecutionResult.Failure($"Doctor diagnostic failed: {ex.Message}", 1);
        }
    }

    private static async Task<string> GetProviderStatusSafe(CommandContext context, CancellationToken cancellationToken)
    {
        try
        {
            return await FormatProviderStatus(context);
        }
        catch
        {
            return "Providers:\n  Unable to enumerate providers\n\n";
        }
    }

    private static async Task<string> GetSessionStatusSafe(CommandContext context, CancellationToken cancellationToken)
    {
        try
        {
            return await FormatSessionStatus(context);
        }
        catch
        {
            return "Sessions:\n  Unable to enumerate sessions\n\n";
        }
    }

    private static string FormatConfigStatus(CommandContext context)
    {
        // We can't easily access the data root from context, so provide general guidance
        var configDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrEmpty(configDir))
        {
            configDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        }

        var clawdNetDir = Path.Combine(configDir, "ClawdNet");
        var configDirPath = Path.Combine(clawdNetDir, "config");

        var lines = new List<string>
        {
            "Configuration:",
            $"  Data root:  {clawdNetDir}",
            $"  Config dir: {configDirPath}",
        };

        // Check for config files
        var configFiles = new[] { "providers.json", "platform.json", "mcp.json", "lsp.json" };
        foreach (var file in configFiles)
        {
            var path = Path.Combine(configDirPath, file);
            var exists = File.Exists(path);
            lines.Add($"  {file,-20} {(exists ? "present" : "not found (using defaults)")}");
        }

        lines.Add(string.Empty);
        return string.Join(Environment.NewLine, lines);
    }

    private static async Task<string> FormatProviderStatus(CommandContext context)
    {
        var lines = new List<string> { "Providers:" };

        try
        {
            var providers = await context.ProviderCatalog.ListAsync(CancellationToken.None);

            if (providers.Count == 0)
            {
                lines.Add("  No providers configured (using built-in defaults)");
            }
            else
            {
                foreach (var provider in providers)
                {
                    var keyStatus = CheckApiKeyStatus(provider);
                    lines.Add($"  {provider.Name,-16} {provider.Kind,-12} {keyStatus}");
                }
            }
        }
        catch
        {
            lines.Add("  Unable to enumerate providers");
        }

        lines.Add(string.Empty);
        return string.Join(Environment.NewLine, lines);
    }

    private static string CheckApiKeyStatus(ProviderDefinition provider)
    {
        var keyEnvVar = provider.ApiKeyEnvironmentVariable;
        if (string.IsNullOrWhiteSpace(keyEnvVar))
        {
            return "no key env var (skip-auth or built-in)";
        }

        var key = Environment.GetEnvironmentVariable(keyEnvVar);
        if (string.IsNullOrWhiteSpace(key))
        {
            return $"not configured ({keyEnvVar} not set)";
        }

        // Redact the key for display
        var displayKey = key.Length > 8
            ? $"{key[..4]}...{key[^4..]}"
            : "****";
        return $"configured ({keyEnvVar}={displayKey})";
    }

    private static async Task<string> FormatSessionStatus(CommandContext context)
    {
        var lines = new List<string> { "Sessions:" };

        try
        {
            var sessions = await context.ConversationStore.ListAsync(CancellationToken.None);
            lines.Add($"  Total sessions: {sessions.Count}");

            if (sessions.Count > 0)
            {
                var mostRecent = sessions.OrderByDescending(s => s.UpdatedAtUtc).First();
                lines.Add($"  Most recent:    {mostRecent.Title} ({mostRecent.Id})");
                lines.Add($"  Updated:        {mostRecent.UpdatedAtUtc.LocalDateTime}");
                lines.Add($"  Messages:       {mostRecent.Messages.Count}");
                if (mostRecent.EffectiveTags.Count > 0)
                {
                    lines.Add($"  Tags:           {string.Join(", ", mostRecent.EffectiveTags)}");
                }
            }
        }
        catch
        {
            lines.Add("  Unable to enumerate sessions");
        }

        lines.Add(string.Empty);
        return string.Join(Environment.NewLine, lines);
    }

    private static string FormatPluginStatus(CommandContext context)
    {
        var lines = new List<string> { "Plugins:" };

        try
        {
            var plugins = context.PluginCatalog.Plugins;
            lines.Add($"  Total plugins: {plugins.Count}");

            foreach (var plugin in plugins)
            {
                var status = plugin.Enabled ? "enabled" : "disabled";
                lines.Add($"  {plugin.Name,-16} {status}");
            }
        }
        catch
        {
            lines.Add("  Unable to enumerate plugins");
        }

        lines.Add(string.Empty);
        return string.Join(Environment.NewLine, lines);
    }

    private static string FormatMcpStatus(CommandContext context)
    {
        var lines = new List<string> { "MCP Servers:" };

        try
        {
            var tools = context.ToolRegistry.Tools
                .Where(t => t.Name.StartsWith("mcp.", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var serverNames = tools
                .Select(t => t.Name.Split('.')[1])
                .Distinct()
                .ToList();

            lines.Add($"  Configured servers: {serverNames.Count}");

            foreach (var name in serverNames)
            {
                var serverTools = tools.Where(t => t.Name.StartsWith($"mcp.{name}.", StringComparison.OrdinalIgnoreCase)).ToList();
                lines.Add($"  {name,-16} {serverTools.Count} tool(s)");
            }

            if (serverNames.Count == 0)
            {
                lines.Add("  No MCP servers configured");
            }
        }
        catch
        {
            lines.Add("  Unable to enumerate MCP servers");
        }

        lines.Add(string.Empty);
        return string.Join(Environment.NewLine, lines);
    }

    private static string FormatLspStatus(CommandContext context)
    {
        var lines = new List<string> { "LSP Servers:" };

        try
        {
            var tools = context.ToolRegistry.Tools
                .Where(t => t.Name.StartsWith("lsp_", StringComparison.OrdinalIgnoreCase))
                .ToList();

            lines.Add($"  LSP tools: {tools.Count}");

            foreach (var tool in tools)
            {
                lines.Add($"  {tool.Name}");
            }

            if (tools.Count == 0)
            {
                lines.Add("  No LSP tools configured");
            }
        }
        catch
        {
            lines.Add("  Unable to enumerate LSP servers");
        }

        lines.Add(string.Empty);
        return string.Join(Environment.NewLine, lines);
    }
}
