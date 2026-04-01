using System.Text.Json.Nodes;

namespace ClawdNet.Core.Models;

public sealed record ToolDefinition(
    string Name,
    string Description,
    JsonObject InputSchema);
