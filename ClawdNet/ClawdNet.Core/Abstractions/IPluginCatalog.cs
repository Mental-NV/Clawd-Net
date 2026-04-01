using ClawdNet.Core.Models;

namespace ClawdNet.Core.Abstractions;

public interface IPluginCatalog
{
    IReadOnlyList<PluginDefinition> Plugins { get; }

    Task ReloadAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<McpServerDefinition>> GetMcpServerDefinitionsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<LspServerDefinition>> GetLspServerDefinitionsAsync(CancellationToken cancellationToken);
}
