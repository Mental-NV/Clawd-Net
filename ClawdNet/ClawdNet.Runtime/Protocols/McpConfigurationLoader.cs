using System.Text.Json;
using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Models;

namespace ClawdNet.Runtime.Protocols;

public sealed class McpConfigurationLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly string _dataRoot;
    private readonly IPluginCatalog? _pluginCatalog;

    public McpConfigurationLoader(string dataRoot, IPluginCatalog? pluginCatalog = null)
    {
        _dataRoot = dataRoot;
        _pluginCatalog = pluginCatalog;
    }

    public string ConfigurationPath => Path.Combine(_dataRoot, "config", "mcp.json");

    public async Task<McpConfiguration> LoadAsync(CancellationToken cancellationToken)
    {
        var servers = new List<McpServerDefinition>();
        if (File.Exists(ConfigurationPath))
        {
            await using var stream = File.OpenRead(ConfigurationPath);
            var payload = await JsonSerializer.DeserializeAsync<McpConfigurationDocument>(stream, JsonOptions, cancellationToken);
            if (payload?.Servers is not null)
            {
                servers.AddRange(payload.Servers
                    .Where(server => !string.IsNullOrWhiteSpace(server.Name) && !string.IsNullOrWhiteSpace(server.Command))
                    .Select(server => new McpServerDefinition(
                        server.Name!,
                        server.Command!,
                        server.Arguments ?? [],
                        server.Environment ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                        server.Enabled ?? true,
                        server.ToolsReadOnly ?? false)));
            }
        }

        if (_pluginCatalog is not null)
        {
            servers.AddRange(await _pluginCatalog.GetMcpServerDefinitionsAsync(cancellationToken));
        }

        return new McpConfiguration(servers.ToArray());
    }

    private sealed class McpConfigurationDocument
    {
        public List<McpServerDocument>? Servers { get; init; }
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
}
