using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Models;

namespace ClawdNet.Tests.TestDoubles;

public sealed class FakePluginCatalog : IPluginCatalog
{
    public IReadOnlyList<PluginDefinition> Plugins { get; set; } = [];

    public IReadOnlyList<McpServerDefinition> McpDefinitions { get; set; } = [];

    public IReadOnlyList<LspServerDefinition> LspDefinitions { get; set; } = [];

    public IReadOnlyList<PluginToolDefinition> ToolDefinitions { get; set; } = [];

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

    public Task<IReadOnlyList<PluginToolDefinition>> GetToolDefinitionsAsync(CancellationToken cancellationToken)
        => Task.FromResult(ToolDefinitions);

    public Task<PluginDefinition> InstallAsync(string sourcePath, CancellationToken cancellationToken)
        => throw new NotImplementedException();

    public Task UninstallAsync(string pluginName, CancellationToken cancellationToken)
        => throw new NotImplementedException();

    public Task<PluginDefinition> EnableAsync(string pluginName, CancellationToken cancellationToken)
        => throw new NotImplementedException();

    public Task<PluginDefinition> DisableAsync(string pluginName, CancellationToken cancellationToken)
        => throw new NotImplementedException();

    public Task UninstallAsync(string pluginName, CancellationToken cancellationToken, bool keepData = false)
        => throw new NotImplementedException();

    public Task<PluginValidationResult> ValidateAsync(string pluginPath, CancellationToken cancellationToken)
        => throw new NotImplementedException();
}
