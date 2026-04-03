using ClawdNet.Core.Models;

namespace ClawdNet.Core.Abstractions;

public interface IPluginCatalog
{
    IReadOnlyList<PluginDefinition> Plugins { get; }

    Task ReloadAsync(CancellationToken cancellationToken);

    Task<PluginDefinition> InstallAsync(string sourcePath, CancellationToken cancellationToken);

    Task UninstallAsync(string pluginName, CancellationToken cancellationToken);

    Task<PluginDefinition> EnableAsync(string pluginName, CancellationToken cancellationToken);

    Task<PluginDefinition> DisableAsync(string pluginName, CancellationToken cancellationToken);

    Task<IReadOnlyList<McpServerDefinition>> GetMcpServerDefinitionsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<LspServerDefinition>> GetLspServerDefinitionsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<PluginToolDefinition>> GetToolDefinitionsAsync(CancellationToken cancellationToken);
}
