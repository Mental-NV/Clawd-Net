using System.Text.Json.Nodes;

namespace ClawdNet.Core.Models;

public abstract record ModelStreamEvent;

public sealed record MessageStartedEvent(string Model) : ModelStreamEvent;

public sealed record TextDeltaEvent(string Text) : ModelStreamEvent;

public sealed record TextCompletedEvent(string Text) : ModelStreamEvent;

public sealed record ToolUseStartedEvent(string Id, string Name) : ModelStreamEvent;

public sealed record ToolUseInputDeltaEvent(string Id, string Name, string PartialJson) : ModelStreamEvent;

public sealed record ToolUseCompletedEvent(string Id, string Name, JsonNode? Input) : ModelStreamEvent;

public sealed record MessageCompletedEvent(string StopReason) : ModelStreamEvent;

public sealed record ModelErrorEvent(string Message) : ModelStreamEvent;
