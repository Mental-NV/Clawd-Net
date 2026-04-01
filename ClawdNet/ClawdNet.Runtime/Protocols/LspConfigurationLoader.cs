using System.Text.Json;
using ClawdNet.Core.Models;

namespace ClawdNet.Runtime.Protocols;

public sealed class LspConfigurationLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly string _dataRoot;

    public LspConfigurationLoader(string dataRoot)
    {
        _dataRoot = dataRoot;
    }

    public string ConfigurationPath => Path.Combine(_dataRoot, "config", "lsp.json");

    public async Task<LspConfiguration> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(ConfigurationPath))
        {
            return new LspConfiguration([]);
        }

        await using var stream = File.OpenRead(ConfigurationPath);
        var payload = await JsonSerializer.DeserializeAsync<LspConfigurationDocument>(stream, JsonOptions, cancellationToken);
        if (payload?.Servers is null)
        {
            return new LspConfiguration([]);
        }

        var servers = payload.Servers
            .Where(server => !string.IsNullOrWhiteSpace(server.Name) && !string.IsNullOrWhiteSpace(server.Command))
            .Select(server => new LspServerDefinition(
                server.Name!,
                server.Command!,
                server.Arguments ?? [],
                server.Environment ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                (server.FileExtensions ?? []).Select(NormalizeExtension).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                server.LanguageId,
                server.Enabled ?? true))
            .ToArray();

        return new LspConfiguration(servers);
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
