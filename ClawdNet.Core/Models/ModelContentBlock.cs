using System.Text.Json.Nodes;

namespace ClawdNet.Core.Models;

public abstract record ModelContentBlock;

public sealed record TextContentBlock(string Text) : ModelContentBlock;

public sealed record ToolUseContentBlock(string Id, string Name, JsonNode? Input) : ModelContentBlock;
