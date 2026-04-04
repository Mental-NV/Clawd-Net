namespace ClawdNet.Core.Models;

public sealed record ModelRequest(
    string Model,
    string SystemPrompt,
    IReadOnlyList<ModelMessage> Messages,
    IReadOnlyList<ToolDefinition> Tools,
    EffortLevel? Effort = null,
    ThinkingMode? Thinking = null);
