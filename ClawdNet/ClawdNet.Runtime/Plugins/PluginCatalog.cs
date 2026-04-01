using System.Text.Json;
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
    private readonly SemaphoreSlim _reloadLock = new(1, 1);
    private IReadOnlyList<PluginDefinition> _plugins = [];
    private bool _loaded;

    public PluginCatalog(string dataRoot)
    {
        _pluginsRoot = Path.Combine(dataRoot, "plugins");
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

        var manifest = new PluginManifest(pluginName, payload.Version, enabled, mcpServers, lspServers);
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
}
