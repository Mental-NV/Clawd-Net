using System.Text.Json.Nodes;

namespace ClawdNet.Core.Models;

public sealed record McpToolDefinition(
    string ServerName,
    string Name,
    string Description,
    JsonObject InputSchema,
    bool ReadOnly);
