using System.Text.Json;
using ClawdNet.Core.Models;

namespace ClawdNet.Runtime.Platform;

public sealed class PlatformConfigurationLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly string _dataRoot;

    public PlatformConfigurationLoader(string dataRoot)
    {
        _dataRoot = dataRoot;
    }

    public string ConfigurationPath => Path.Combine(_dataRoot, "config", "platform.json");

    public async Task<PlatformConfiguration> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(ConfigurationPath))
        {
            return new PlatformConfiguration();
        }

        await using var stream = File.OpenRead(ConfigurationPath);
        var document = await JsonSerializer.DeserializeAsync<PlatformConfigurationDocument>(stream, JsonOptions, cancellationToken);
        return new PlatformConfiguration(
            Normalize(document?.EditorCommand),
            document?.EditorArguments ?? [],
            Normalize(document?.BrowserCommand),
            document?.BrowserArguments ?? [],
            Normalize(document?.RevealCommand),
            document?.RevealArguments ?? []);
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed class PlatformConfigurationDocument
    {
        public string? EditorCommand { get; init; }

        public string[]? EditorArguments { get; init; }

        public string? BrowserCommand { get; init; }

        public string[]? BrowserArguments { get; init; }

        public string? RevealCommand { get; init; }

        public string[]? RevealArguments { get; init; }
    }
}
