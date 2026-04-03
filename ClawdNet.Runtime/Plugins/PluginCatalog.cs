using System.Text.Json;
using System.Text.Json.Nodes;
using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Models;

namespace ClawdNet.Runtime.Plugins;

public sealed class PluginCatalog : IPluginCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly string _pluginsRoot;
    private readonly HashSet<string> _reservedCommandNames;
    private readonly HashSet<string> _reservedToolNames;
    private readonly SemaphoreSlim _reloadLock = new(1, 1);
    private IReadOnlyList<PluginDefinition> _plugins = [];
    private bool _loaded;

    public PluginCatalog(string dataRoot, IEnumerable<string>? reservedCommandNames = null, IEnumerable<string>? reservedToolNames = null)
    {
        _pluginsRoot = Path.Combine(dataRoot, "plugins");
        _reservedCommandNames = new HashSet<string>(reservedCommandNames ?? [], StringComparer.OrdinalIgnoreCase);
        _reservedToolNames = new HashSet<string>(reservedToolNames ?? [], StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<PluginDefinition> Plugins => _plugins;

    public async Task ReloadAsync(CancellationToken cancellationToken)
    {
        await _reloadLock.WaitAsync(cancellationToken);
        try
        {
            _plugins = await LoadPluginsAsync(cancellationToken);
            _loaded = true;
        }
        finally
        {
            _reloadLock.Release();
        }
    }

    public async Task<IReadOnlyList<McpServerDefinition>> GetMcpServerDefinitionsAsync(CancellationToken cancellationToken)
    {
        await EnsureLoadedAsync(cancellationToken);
        return _plugins
            .Where(plugin => plugin.Enabled && plugin.IsValid)
            .SelectMany(plugin => plugin.McpServers)
            .ToArray();
    }

    public async Task<IReadOnlyList<LspServerDefinition>> GetLspServerDefinitionsAsync(CancellationToken cancellationToken)
    {
        await EnsureLoadedAsync(cancellationToken);
        return _plugins
            .Where(plugin => plugin.Enabled && plugin.IsValid)
            .SelectMany(plugin => plugin.LspServers)
            .ToArray();
    }

    public async Task<IReadOnlyList<PluginToolDefinition>> GetToolDefinitionsAsync(CancellationToken cancellationToken)
    {
        await EnsureLoadedAsync(cancellationToken);
        return _plugins
            .Where(plugin => plugin.Enabled && plugin.IsValid)
            .SelectMany(plugin => plugin.Tools.Where(tool => tool.Enabled))
            .ToArray();
    }

    public async Task<PluginDefinition> InstallAsync(string sourcePath, CancellationToken cancellationToken)
    {
        // Validate source has plugin.json
        var sourceManifest = Path.Combine(sourcePath, "plugin.json");
        if (!File.Exists(sourceManifest))
        {
            throw new InvalidOperationException($"Source path '{sourcePath}' does not contain a valid plugin.json manifest.");
        }

        // Read manifest to get plugin name
        PluginManifestDocument? payload;
        await using (var stream = File.OpenRead(sourceManifest))
        {
            payload = await JsonSerializer.DeserializeAsync<PluginManifestDocument>(stream, JsonOptions, cancellationToken);
        }

        if (payload is null || string.IsNullOrWhiteSpace(payload.Name))
        {
            throw new InvalidOperationException("Plugin manifest must include a non-empty 'name'.");
        }

        var pluginName = payload.Name.Trim();
        var targetDir = Path.Combine(_pluginsRoot, pluginName);

        // Copy directory to plugins root
        CopyDirectory(sourcePath, targetDir, overwrite: true);

        // Reload catalog
        await ReloadAsync(cancellationToken);

        return _plugins.FirstOrDefault(p => p.Name == pluginName)
            ?? throw new InvalidOperationException($"Plugin '{pluginName}' was installed but not found after reload.");
    }

    public async Task UninstallAsync(string pluginName, CancellationToken cancellationToken)
    {
        await EnsureLoadedAsync(cancellationToken);

        var plugin = _plugins.FirstOrDefault(p =>
            string.Equals(p.Name, pluginName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(p.Id, pluginName, StringComparison.OrdinalIgnoreCase));

        if (plugin is null)
        {
            throw new InvalidOperationException($"Plugin '{pluginName}' was not found.");
        }

        // Remove plugin directory
        if (Directory.Exists(plugin.Path))
        {
            Directory.Delete(plugin.Path, recursive: true);
        }

        // Reload catalog
        await ReloadAsync(cancellationToken);
    }

    public async Task<PluginDefinition> EnableAsync(string pluginName, CancellationToken cancellationToken)
    {
        return await SetEnabledAsync(pluginName, enabled: true, cancellationToken);
    }

    public async Task<PluginDefinition> DisableAsync(string pluginName, CancellationToken cancellationToken)
    {
        return await SetEnabledAsync(pluginName, enabled: false, cancellationToken);
    }

    private async Task<PluginDefinition> SetEnabledAsync(string pluginName, bool enabled, CancellationToken cancellationToken)
    {
        await EnsureLoadedAsync(cancellationToken);

        var plugin = _plugins.FirstOrDefault(p =>
            string.Equals(p.Name, pluginName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(p.Id, pluginName, StringComparison.OrdinalIgnoreCase));

        if (plugin is null)
        {
            throw new InvalidOperationException($"Plugin '{pluginName}' was not found.");
        }

        // Update plugin.json
        var manifestPath = Path.Combine(plugin.Path, "plugin.json");
        var json = await File.ReadAllTextAsync(manifestPath, cancellationToken);
        var doc = JsonNode.Parse(json) ?? throw new InvalidOperationException("Invalid plugin.json");
        doc["enabled"] = enabled;
        await File.WriteAllTextAsync(manifestPath, doc.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), cancellationToken);

        // Reload catalog
        await ReloadAsync(cancellationToken);

        return _plugins.FirstOrDefault(p => p.Name == plugin.Name)
            ?? throw new InvalidOperationException($"Plugin '{pluginName}' was updated but not found after reload.");
    }

    private static void CopyDirectory(string sourceDir, string targetDir, bool overwrite)
    {
        Directory.CreateDirectory(targetDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var targetFile = Path.Combine(targetDir, Path.GetFileName(file));
            File.Copy(file, targetFile, overwrite);
        }

        foreach (var subDir in Directory.GetDirectories(sourceDir))
        {
            var targetSubDir = Path.Combine(targetDir, Path.GetFileName(subDir));
            CopyDirectory(subDir, targetSubDir, overwrite);
        }
    }

    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (_loaded)
        {
            return;
        }

        await ReloadAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<PluginDefinition>> LoadPluginsAsync(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_pluginsRoot))
        {
            return [];
        }

        var plugins = new List<PluginDefinition>();
        foreach (var pluginDirectory in Directory.GetDirectories(_pluginsRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();
            plugins.Add(await LoadPluginAsync(pluginDirectory, cancellationToken));
        }

        return plugins
            .OrderBy(plugin => plugin.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task<PluginDefinition> LoadPluginAsync(string pluginDirectory, CancellationToken cancellationToken)
    {
        var manifestPath = Path.Combine(pluginDirectory, "plugin.json");
        var pluginId = Path.GetFileName(pluginDirectory);
        var errors = new List<PluginError>();

        if (!File.Exists(manifestPath))
        {
            errors.Add(new PluginError("manifest-missing", "plugin.json was not found."));
            return new PluginDefinition(pluginId, pluginId, pluginDirectory, false, null, errors);
        }

        PluginManifestDocument? payload;
        try
        {
            await using var stream = File.OpenRead(manifestPath);
            payload = await JsonSerializer.DeserializeAsync<PluginManifestDocument>(stream, JsonOptions, cancellationToken);
        }
        catch (JsonException ex)
        {
            errors.Add(new PluginError("manifest-invalid", ex.Message));
            return new PluginDefinition(pluginId, pluginId, pluginDirectory, false, null, errors);
        }

        if (payload is null || string.IsNullOrWhiteSpace(payload.Name))
        {
            errors.Add(new PluginError("manifest-invalid", "Plugin manifest must include a non-empty 'name'."));
            return new PluginDefinition(pluginId, pluginId, pluginDirectory, false, null, errors);
        }

        var pluginName = payload.Name!.Trim();
        var enabled = payload.Enabled ?? true;
        var commands = new List<PluginCommandDefinition>();
        foreach (var command in payload.Commands ?? [])
        {
            if (string.IsNullOrWhiteSpace(command.Name))
            {
                errors.Add(new PluginError("command-invalid", "Plugin command must include a non-empty 'name'."));
                continue;
            }

            if (string.IsNullOrWhiteSpace(command.Command))
            {
                errors.Add(new PluginError("command-invalid", $"Plugin command '{command.Name}' must include a non-empty 'command'."));
                continue;
            }

            if (_reservedCommandNames.Contains(command.Name))
            {
                errors.Add(new PluginError("command-conflict", $"Plugin command '{command.Name}' conflicts with a built-in command."));
                continue;
            }

            commands.Add(new PluginCommandDefinition(
                command.Name.Trim(),
                command.Command!,
                command.Arguments ?? [],
                command.Environment ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                PluginExecutionMode.Subprocess,
                command.Enabled ?? true));
        }

        var tools = new List<PluginToolDefinition>();
        foreach (var tool in payload.Tools ?? [])
        {
            if (string.IsNullOrWhiteSpace(tool.Name))
            {
                errors.Add(new PluginError("tool-invalid", "Plugin tool must include a non-empty 'name'."));
                continue;
            }

            if (string.IsNullOrWhiteSpace(tool.Command))
            {
                errors.Add(new PluginError("tool-invalid", $"Plugin tool '{tool.Name}' must include a non-empty 'command'."));
                continue;
            }

            if (_reservedToolNames.Contains(tool.Name))
            {
                errors.Add(new PluginError("tool-conflict", $"Plugin tool '{tool.Name}' conflicts with a built-in tool."));
                continue;
            }

            if (tool.InputSchema is null || tool.InputSchema["type"]?.GetValue<string>() is not "object")
            {
                errors.Add(new PluginError("tool-invalid", $"Plugin tool '{tool.Name}' must include an object-shaped 'inputSchema'."));
                continue;
            }

            tools.Add(new PluginToolDefinition(
                tool.Name.Trim(),
                tool.Description?.Trim() ?? string.Empty,
                tool.InputSchema,
                ParseToolCategory(tool.Category),
                tool.Command!,
                tool.Arguments ?? [],
                tool.Environment ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                PluginExecutionMode.Subprocess,
                tool.Enabled ?? true));
        }

        var hooks = new List<PluginHookDefinition>();
        foreach (var hook in payload.Hooks ?? [])
        {
            if (string.IsNullOrWhiteSpace(hook.Kind) || !TryParseHookKind(hook.Kind, out var kind))
            {
                errors.Add(new PluginError("hook-invalid", "Plugin hook must include a supported 'kind'."));
                continue;
            }

            if (string.IsNullOrWhiteSpace(hook.Command))
            {
                errors.Add(new PluginError("hook-invalid", $"Plugin hook '{hook.Kind}' must include a non-empty 'command'."));
                continue;
            }

            hooks.Add(new PluginHookDefinition(
                kind,
                hook.Command!,
                hook.Arguments ?? [],
                hook.Environment ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                PluginExecutionMode.Subprocess,
                hook.Enabled ?? true,
                hook.Blocking ?? false));
        }

        var mcpServers = (payload.McpServers ?? [])
            .Where(server => !string.IsNullOrWhiteSpace(server.Name) && !string.IsNullOrWhiteSpace(server.Command))
            .Select(server => new McpServerDefinition(
                $"{pluginName}.{server.Name}",
                server.Command!,
                server.Arguments ?? [],
                server.Environment ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                server.Enabled ?? true,
                server.ToolsReadOnly ?? false))
            .ToArray();
        var lspServers = (payload.LspServers ?? [])
            .Where(server => !string.IsNullOrWhiteSpace(server.Name) && !string.IsNullOrWhiteSpace(server.Command))
            .Select(server => new LspServerDefinition(
                $"{pluginName}.{server.Name}",
                server.Command!,
                server.Arguments ?? [],
                server.Environment ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                (server.FileExtensions ?? []).Select(NormalizeExtension).Where(extension => !string.IsNullOrWhiteSpace(extension)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                server.LanguageId,
                server.Enabled ?? true))
            .ToArray();

        var manifest = new PluginManifest(pluginName, payload.Version, enabled, mcpServers, lspServers, tools, commands, hooks);
        return new PluginDefinition(pluginId, pluginName, pluginDirectory, enabled, manifest, errors);
    }

    private static string NormalizeExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return string.Empty;
        }

        return extension.StartsWith(".", StringComparison.Ordinal) ? extension : $".{extension}";
    }

    private sealed class PluginManifestDocument
    {
        public string? Name { get; init; }
        public string? Version { get; init; }
        public bool? Enabled { get; init; }
        public List<McpServerDocument>? McpServers { get; init; }
        public List<LspServerDocument>? LspServers { get; init; }
        public List<PluginToolDocument>? Tools { get; init; }
        public List<PluginCommandDocument>? Commands { get; init; }
        public List<PluginHookDocument>? Hooks { get; init; }
    }

    private sealed class McpServerDocument
    {
        public string? Name { get; init; }
        public string? Command { get; init; }
        public string[]? Arguments { get; init; }
        public Dictionary<string, string>? Environment { get; init; }
        public bool? Enabled { get; init; }
        public bool? ToolsReadOnly { get; init; }
    }

    private sealed class LspServerDocument
    {
        public string? Name { get; init; }
        public string? Command { get; init; }
        public string[]? Arguments { get; init; }
        public Dictionary<string, string>? Environment { get; init; }
        public string[]? FileExtensions { get; init; }
        public string? LanguageId { get; init; }
        public bool? Enabled { get; init; }
    }

    private sealed class PluginCommandDocument
    {
        public string? Name { get; init; }
        public string? Command { get; init; }
        public string[]? Arguments { get; init; }
        public Dictionary<string, string>? Environment { get; init; }
        public bool? Enabled { get; init; }
    }

    private sealed class PluginToolDocument
    {
        public string? Name { get; init; }
        public string? Description { get; init; }
        public JsonObject? InputSchema { get; init; }
        public string? Category { get; init; }
        public string? Command { get; init; }
        public string[]? Arguments { get; init; }
        public Dictionary<string, string>? Environment { get; init; }
        public bool? Enabled { get; init; }
    }

    private sealed class PluginHookDocument
    {
        public string? Kind { get; init; }
        public string? Command { get; init; }
        public string[]? Arguments { get; init; }
        public Dictionary<string, string>? Environment { get; init; }
        public bool? Enabled { get; init; }
        public bool? Blocking { get; init; }
    }

    private static bool TryParseHookKind(string value, out PluginHookKind kind)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "beforequery":
            case "before-query":
                kind = PluginHookKind.BeforeQuery;
                return true;
            case "afterquery":
            case "after-query":
                kind = PluginHookKind.AfterQuery;
                return true;
            case "aftertoolresult":
            case "after-tool-result":
                kind = PluginHookKind.AfterToolResult;
                return true;
            case "aftertaskcompletion":
            case "after-task-completion":
                kind = PluginHookKind.AfterTaskCompletion;
                return true;
            default:
                kind = default;
                return false;
        }
    }

    private static ToolCategory ParseToolCategory(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "readonly" or "read-only" or "read" => ToolCategory.ReadOnly,
            "write" => ToolCategory.Write,
            "execute" or "exec" => ToolCategory.Execute,
            _ => ToolCategory.Execute
        };
    }
}
