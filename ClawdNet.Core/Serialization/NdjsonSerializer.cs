using System.Text.Json;
using ClawdNet.Core.Models;

namespace ClawdNet.Core.Serialization;

/// <summary>
/// Serializes QueryStreamEvent instances to NDJSON-compatible SDK message format.
/// Maps internal events to the legacy StdoutMessage shape for --output-format=stream-json.
/// </summary>
public static class NdjsonSerializer
{
    /// <summary>
    /// Serializes a single QueryStreamEvent to an NDJSON line.
    /// Returns null for events that should not be emitted to the stream.
    /// </summary>
    public static string? Serialize(QueryStreamEvent streamEvent)
    {
        return streamEvent switch
        {
            UserTurnAcceptedEvent e => SerializeUserTurn(e),
            AssistantTextDeltaStreamEvent e => SerializeAssistantDelta(e),
            AssistantMessageCommittedEvent e => SerializeAssistantCommitted(e),
            ToolCallRequestedEvent e => SerializeToolCallRequested(e),
            PermissionDecisionStreamEvent e => SerializePermissionDecision(e),
            ToolResultCommittedEvent e => SerializeToolResult(e),
            TaskStartedStreamEvent e => SerializeTaskStarted(e),
            TaskUpdatedStreamEvent e => SerializeTaskUpdated(e),
            TaskCompletedStreamEvent e => SerializeTaskCompleted(e),
            TaskFailedStreamEvent e => SerializeTaskFailed(e),
            TaskCanceledStreamEvent e => SerializeTaskCanceled(e),
            TurnCompletedStreamEvent e => SerializeTurnCompleted(e),
            TurnFailedStreamEvent e => SerializeTurnFailed(e),
            // PluginHookRecordedEvent and EditPreviewGeneratedEvent/EditApprovalRecordedEvent
            // are internal events not typically exposed in stream-json mode.
            // They can be added later if --include-hook-events is implemented.
            _ => null
        };
    }

    private static string SerializeUserTurn(UserTurnAcceptedEvent e)
    {
        return JsonSerializer.Serialize(new
        {
            type = "system",
            subtype = "user_turn_accepted",
            sessionId = e.Session.Id
        });
    }

    private static string SerializeAssistantDelta(AssistantTextDeltaStreamEvent e)
    {
        return JsonSerializer.Serialize(new
        {
            type = "assistant",
            delta = e.DeltaText
        });
    }

    private static string SerializeAssistantCommitted(AssistantMessageCommittedEvent e)
    {
        return JsonSerializer.Serialize(new
        {
            type = "assistant",
            committed = true,
            message = e.MessageText,
            sessionId = e.Session.Id
        });
    }

    private static string SerializeToolCallRequested(ToolCallRequestedEvent e)
    {
        return JsonSerializer.Serialize(new
        {
            type = "system",
            subtype = "tool_call_requested",
            tool = e.ToolCall.Name,
            toolUseId = e.ToolCall.Id
        });
    }

    private static string SerializePermissionDecision(PermissionDecisionStreamEvent e)
    {
        return JsonSerializer.Serialize(new
        {
            type = "system",
            subtype = "permission_decision",
            tool = e.ToolCall.Name,
            toolUseId = e.ToolCall.Id,
            decision = e.Decision.Kind.ToString().ToLowerInvariant(),
            approved = e.Approved
        });
    }

    private static string SerializeToolResult(ToolResultCommittedEvent e)
    {
        return JsonSerializer.Serialize(new
        {
            type = "system",
            subtype = "tool_result_committed",
            tool = e.ToolCall.Name,
            toolUseId = e.ToolCall.Id,
            success = e.Result.Success,
            sessionId = e.Session.Id
        });
    }

    private static string SerializeTaskStarted(TaskStartedStreamEvent e)
    {
        return JsonSerializer.Serialize(new
        {
            type = "system",
            subtype = "task_started",
            taskId = e.Task.Id,
            goal = e.Task.Goal
        });
    }

    private static string SerializeTaskUpdated(TaskUpdatedStreamEvent e)
    {
        return JsonSerializer.Serialize(new
        {
            type = "system",
            subtype = "task_updated",
            taskId = e.Task.Id,
            status = e.Event.Status.ToString().ToLowerInvariant()
        });
    }

    private static string SerializeTaskCompleted(TaskCompletedStreamEvent e)
    {
        return JsonSerializer.Serialize(new
        {
            type = "system",
            subtype = "task_completed",
            taskId = e.Task.Id,
            success = e.Result.Success
        });
    }

    private static string SerializeTaskFailed(TaskFailedStreamEvent e)
    {
        return JsonSerializer.Serialize(new
        {
            type = "system",
            subtype = "task_failed",
            taskId = e.Task.Id,
            success = e.Result.Success
        });
    }

    private static string SerializeTaskCanceled(TaskCanceledStreamEvent e)
    {
        return JsonSerializer.Serialize(new
        {
            type = "system",
            subtype = "task_canceled",
            taskId = e.Task.Id
        });
    }

    private static string SerializeTurnCompleted(TurnCompletedStreamEvent e)
    {
        return JsonSerializer.Serialize(new
        {
            type = "result",
            subtype = "success",
            sessionId = e.Result.Session.Id,
            provider = e.Result.Session.Provider,
            model = e.Result.Session.Model,
            turnsExecuted = e.Result.TurnsExecuted,
            assistantText = e.Result.AssistantText
        });
    }

    private static string SerializeTurnFailed(TurnFailedStreamEvent e)
    {
        return JsonSerializer.Serialize(new
        {
            type = "result",
            subtype = "error",
            message = e.Message
        });
    }
}
