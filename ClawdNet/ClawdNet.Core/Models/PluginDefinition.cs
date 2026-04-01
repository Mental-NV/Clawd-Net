namespace ClawdNet.Core.Models;

public sealed record PluginDefinition(
    string Id,
    string Name,
    string Path,
    bool Enabled,
    PluginManifest? Manifest,
    IReadOnlyList<PluginError> Errors)
{
    public bool IsValid => Manifest is not null && !Errors.Any(IsFatalError);

    public IReadOnlyList<McpServerDefinition> McpServers => Manifest?.McpServers ?? [];

    public IReadOnlyList<LspServerDefinition> LspServers => Manifest?.LspServers ?? [];

    public IReadOnlyList<PluginCommandDefinition> Commands => Manifest?.Commands ?? [];

    public IReadOnlyList<PluginHookDefinition> Hooks => Manifest?.Hooks ?? [];

    private static bool IsFatalError(PluginError error)
    {
        return error.Code.StartsWith("manifest-", StringComparison.OrdinalIgnoreCase);
    }
}
