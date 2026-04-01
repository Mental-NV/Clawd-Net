using System.Text.Json.Nodes;

namespace ClawdNet.Core.Models;

public sealed record ToolExecutionRequest(
    string ToolName,
    JsonNode? Input = null,
    string? RawInput = null);
