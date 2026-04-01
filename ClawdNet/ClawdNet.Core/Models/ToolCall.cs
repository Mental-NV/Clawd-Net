using System.Text.Json.Nodes;

namespace ClawdNet.Core.Models;

public sealed record ToolCall(
    string Id,
    string Name,
    JsonNode? Input);
