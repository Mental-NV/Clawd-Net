using System.Text.Json.Nodes;

namespace ClawdNet.Core.Models;

public sealed record PluginToolInvocation(
    PluginDefinition Plugin,
    PluginToolDefinition Tool,
    string QualifiedToolName,
    JsonNode? Input,
    string? RawInput,
    string? SessionId,
    string? TaskId,
    string? WorkingDirectory);
