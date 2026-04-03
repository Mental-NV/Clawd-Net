using System.Text.Json.Nodes;

namespace ClawdNet.Core.Models;

public sealed record PluginToolDefinition(
    string Name,
    string Description,
    JsonObject InputSchema,
    ToolCategory Category,
    string Command,
    IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string> Environment,
    PluginExecutionMode ExecutionMode,
    bool Enabled);
