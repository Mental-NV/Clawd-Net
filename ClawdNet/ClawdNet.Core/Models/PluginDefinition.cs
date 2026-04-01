namespace ClawdNet.Core.Models;

public sealed record PluginDefinition(
    string Id,
    string Name,
    string Path,
    bool Enabled,
    PluginManifest? Manifest,
    IReadOnlyList<PluginError> Errors)
{
    public bool IsValid => Errors.Count == 0 && Manifest is not null;

    public IReadOnlyList<McpServerDefinition> McpServers => Manifest?.McpServers ?? [];

    public IReadOnlyList<LspServerDefinition> LspServers => Manifest?.LspServers ?? [];
}
