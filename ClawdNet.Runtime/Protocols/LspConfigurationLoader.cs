using System.Text.Json;
using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Models;

namespace ClawdNet.Runtime.Protocols;

public sealed class LspConfigurationLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly string _dataRoot;
    private readonly IPluginCatalog? _pluginCatalog;

    public LspConfigurationLoader(string dataRoot, IPluginCatalog? pluginCatalog = null)
    {
        _dataRoot = dataRoot;
        _pluginCatalog = pluginCatalog;
    }

    public string ConfigurationPath => Path.Combine(_dataRoot, "config", "lsp.json");

    public async Task<LspConfiguration> LoadAsync(CancellationToken cancellationToken)
    {
        var servers = new List<LspServerDefinition>();
        if (File.Exists(ConfigurationPath))
        {
            await using var stream = File.OpenRead(ConfigurationPath);
            var payload = await JsonSerializer.DeserializeAsync<LspConfigurationDocument>(stream, JsonOptions, cancellationToken);
            if (payload?.Servers is not null)
            {
                servers.AddRange(payload.Servers
                    .Where(server => !string.IsNullOrWhiteSpace(server.Name) && !string.IsNullOrWhiteSpace(server.Command))
                    .Select(server => new LspServerDefinition(
                        server.Name!,
                        server.Command!,
                        server.Arguments ?? [],
                        server.Environment ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                        (server.FileExtensions ?? []).Select(NormalizeExtension).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                        server.LanguageId,
                        server.Enabled ?? true)));
            }
        }

        if (_pluginCatalog is not null)
        {
            servers.AddRange(await _pluginCatalog.GetLspServerDefinitionsAsync(cancellationToken));
        }

        return new LspConfiguration(servers.ToArray());
    }

    private static string NormalizeExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return string.Empty;
        }

        return extension.StartsWith(".", StringComparison.Ordinal) ? extension : $".{extension}";
    }

    private sealed class LspConfigurationDocument
    {
        public List<LspServerDocument>? Servers { get; init; }
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
