using System.Text.Json;
using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Models;

namespace ClawdNet.Runtime.Protocols;

public sealed class McpConfigurationLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private readonly string _dataRoot;
    private readonly IPluginCatalog? _pluginCatalog;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

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
                    .Where(server => !string.IsNullOrWhiteSpace(server.Name))
                    .Select(ToServerDefinition));
            }
        }

        if (_pluginCatalog is not null)
        {
            servers.AddRange(await _pluginCatalog.GetMcpServerDefinitionsAsync(cancellationToken));
        }

        return new McpConfiguration(servers.ToArray());
    }

    public async Task SaveAsync(McpConfiguration configuration, CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigurationPath)!);
            var document = new McpConfigurationDocument
            {
                Servers = configuration.Servers.Select(ToServerDocument).ToList(),
            };

            await using var stream = File.Create(ConfigurationPath);
            await JsonSerializer.SerializeAsync(stream, document, JsonOptions, cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task AddServerAsync(McpServerDefinition server, CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var config = await LoadAsync(cancellationToken);
            var existingServers = config.Servers.ToList();
            var index = existingServers.FindIndex(s => string.Equals(s.Name, server.Name, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                existingServers[index] = server;
            }
            else
            {
                existingServers.Add(server);
            }

            await SaveAsync(new McpConfiguration(existingServers.ToArray()), cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<bool> RemoveServerAsync(string name, CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var config = await LoadAsync(cancellationToken);
            var existingServers = config.Servers.ToList();
            var removed = existingServers.RemoveAll(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
            if (removed > 0)
            {
                await SaveAsync(new McpConfiguration(existingServers.ToArray()), cancellationToken);
                return true;
            }
            return false;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private static McpServerDefinition ToServerDefinition(McpServerDocument doc)
    {
        var transport = Enum.TryParse<McpTransportType>(doc.Transport, ignoreCase: true, out var parsedTransport)
            ? parsedTransport
            : McpTransportType.Stdio;

        return new McpServerDefinition(
            doc.Name!,
            doc.Command ?? string.Empty,
            doc.Arguments ?? [],
            doc.Environment ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            doc.Enabled ?? true,
            doc.ToolsReadOnly ?? false,
            transport,
            doc.Url,
            doc.Headers ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
    }

    private static McpServerDocument ToServerDocument(McpServerDefinition server)
    {
        return new McpServerDocument
        {
            Name = server.Name,
            Command = server.Command,
            Arguments = server.Arguments.ToList(),
            Environment = server.Environment.Count > 0 ? new Dictionary<string, string>(server.Environment, StringComparer.OrdinalIgnoreCase) : null,
            Enabled = server.Enabled,
            ToolsReadOnly = server.ToolsReadOnly,
            Transport = server.Transport.ToString(),
            Url = server.Url,
            Headers = server.Headers?.Count > 0 ? new Dictionary<string, string>(server.Headers, StringComparer.OrdinalIgnoreCase) : null,
        };
    }

    private sealed class McpConfigurationDocument
    {
        public List<McpServerDocument>? Servers { get; init; }
    }

    private sealed class McpServerDocument
    {
        public string? Name { get; init; }

        public string? Command { get; init; }

        public List<string>? Arguments { get; init; }

        public Dictionary<string, string>? Environment { get; init; }

        public bool? Enabled { get; init; }

        public bool? ToolsReadOnly { get; init; }

        public string? Transport { get; init; }

        public string? Url { get; init; }

        public Dictionary<string, string>? Headers { get; init; }
    }
}
