namespace ClawdNet.Core.Models;

public sealed record PluginManifest(
    string Name,
    string? Version,
    bool Enabled,
    IReadOnlyList<McpServerDefinition> McpServers,
    IReadOnlyList<LspServerDefinition> LspServers);
