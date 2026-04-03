namespace ClawdNet.Core.Models;

public sealed record QueryExecutionResult(
    ConversationSession Session,
    string AssistantText,
    int TurnsExecuted);
