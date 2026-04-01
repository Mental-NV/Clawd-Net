namespace ClawdNet.Core.Models;

public abstract record QueryStreamEvent;

public sealed record UserTurnAcceptedEvent(ConversationSession Session) : QueryStreamEvent;

public sealed record AssistantTextDeltaStreamEvent(string DeltaText) : QueryStreamEvent;

public sealed record AssistantMessageCommittedEvent(ConversationSession Session, string MessageText) : QueryStreamEvent;

public sealed record ToolCallRequestedEvent(ToolCall ToolCall) : QueryStreamEvent;

public sealed record PermissionDecisionStreamEvent(ToolCall ToolCall, PermissionDecision Decision, bool? Approved = null) : QueryStreamEvent;

public sealed record EditPreviewGeneratedEvent(ConversationSession Session, ToolCall ToolCall, EditPreview Preview) : QueryStreamEvent;

public sealed record EditApprovalRecordedEvent(ConversationSession Session, ToolCall ToolCall, bool Approved, string Summary) : QueryStreamEvent;

public sealed record ToolResultCommittedEvent(ConversationSession Session, ToolCall ToolCall, ToolExecutionResult Result) : QueryStreamEvent;

public sealed record TurnCompletedStreamEvent(QueryExecutionResult Result) : QueryStreamEvent;

public sealed record TurnFailedStreamEvent(string Message) : QueryStreamEvent;
