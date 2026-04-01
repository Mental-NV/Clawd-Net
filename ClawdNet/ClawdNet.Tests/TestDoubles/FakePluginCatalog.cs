using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Models;

namespace ClawdNet.Tests.TestDoubles;

public sealed class FakePluginCatalog : IPluginCatalog
{
    public IReadOnlyList<PluginDefinition> Plugins { get; set; } = [];

    public IReadOnlyList<McpServerDefinition> McpDefinitions { get; set; } = [];

    public IReadOnlyList<LspServerDefinition> LspDefinitions { get; set; } = [];

    public int ReloadCount { get; private set; }

    public Func<FakePluginCatalog, Task>? ReloadHandler { get; set; }

    public async Task ReloadAsync(CancellationToken cancellationToken)
    {
        ReloadCount++;
        if (ReloadHandler is not null)
        {
            await ReloadHandler(this);
        }
    }

    public Task<IReadOnlyList<McpServerDefinition>> GetMcpServerDefinitionsAsync(CancellationToken cancellationToken)
        => Task.FromResult(McpDefinitions);

    public Task<IReadOnlyList<LspServerDefinition>> GetLspServerDefinitionsAsync(CancellationToken cancellationToken)
        => Task.FromResult(LspDefinitions);
}
