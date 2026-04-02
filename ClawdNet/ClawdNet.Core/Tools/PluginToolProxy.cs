using System.Text.Json.Nodes;
using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Models;

namespace ClawdNet.Core.Tools;

public sealed class PluginToolProxy : ITool
{
    private readonly IPluginRuntime _pluginRuntime;
    private readonly PluginDefinition _plugin;
    private readonly PluginToolDefinition _definition;

    public PluginToolProxy(IPluginRuntime pluginRuntime, PluginDefinition plugin, PluginToolDefinition definition)
    {
        _pluginRuntime = pluginRuntime;
        _plugin = plugin;
        _definition = definition;
        Name = $"plugin.{plugin.Name}.{definition.Name}";
        Description = string.IsNullOrWhiteSpace(definition.Description)
            ? $"[{plugin.Name}] plugin tool '{definition.Name}'."
            : $"[{plugin.Name}] {definition.Description}";
        InputSchema = definition.InputSchema;
        Category = definition.Category;
    }

    public string Name { get; }

    public string Description { get; }

    public ToolCategory Category { get; }

    public JsonObject InputSchema { get; }

    public Task<ToolExecutionResult> ExecuteAsync(ToolExecutionRequest request, CancellationToken cancellationToken)
    {
        return _pluginRuntime.ExecuteToolAsync(
            new PluginToolInvocation(
                _plugin,
                _definition,
                Name,
                request.Input,
                request.RawInput,
                request.SessionId,
                null,
                Environment.CurrentDirectory),
            cancellationToken);
    }
}
