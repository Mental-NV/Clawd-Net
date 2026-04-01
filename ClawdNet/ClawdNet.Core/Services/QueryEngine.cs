using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Exceptions;
using ClawdNet.Core.Models;
using System.Text.Json;

namespace ClawdNet.Core.Services;

public sealed class QueryEngine : IQueryEngine
{
    private const string DefaultModel = "claude-sonnet-4-5";
    private const string DefaultSystemPrompt = "You are ClawdNet, a concise coding assistant.";

    private readonly IConversationStore _conversationStore;
    private readonly IAnthropicMessageClient _anthropicMessageClient;
    private readonly IToolRegistry _toolRegistry;
    private readonly IToolExecutor _toolExecutor;
    private readonly IPermissionService _permissionService;
    private readonly IPluginRuntime _pluginRuntime;

    public QueryEngine(
        IConversationStore conversationStore,
        IAnthropicMessageClient anthropicMessageClient,
        IToolRegistry toolRegistry,
        IToolExecutor toolExecutor,
        IPermissionService permissionService,
        IPluginRuntime? pluginRuntime = null)
    {
        _conversationStore = conversationStore;
        _anthropicMessageClient = anthropicMessageClient;
        _toolRegistry = toolRegistry;
        _toolExecutor = toolExecutor;
        _permissionService = permissionService;
        _pluginRuntime = pluginRuntime ?? new NoOpPluginRuntime();
    }

    public async Task<QueryExecutionResult> AskAsync(QueryRequest request, CancellationToken cancellationToken)
    {
        QueryExecutionResult? result = null;

        await foreach (var streamEvent in StreamAskAsync(request, cancellationToken))
        {
            switch (streamEvent)
            {
                case TurnCompletedStreamEvent completed:
                    result = completed.Result;
                    break;
                case TurnFailedStreamEvent failed:
                    throw new ConversationStoreException(failed.Message);
            }
        }

        return result ?? throw new InvalidOperationException("Streaming query completed without a final result.");
    }

    public async IAsyncEnumerable<QueryStreamEvent> StreamAskAsync(
        QueryRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var session = await LoadOrCreateSessionAsync(request, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var workingMessages = session.Messages.ToList();
        workingMessages.Add(new ConversationMessage("user", request.Prompt, now));
        session = session with
        {
            UpdatedAtUtc = now,
            Messages = workingMessages.ToArray()
        };

        var beforeQueryHooks = await EmitHookResultsAsync(
            session,
            workingMessages,
            PluginHookKind.BeforeQuery,
            new
            {
                prompt = request.Prompt,
                model = session.Model,
                permissionMode = request.PermissionMode.ToString(),
                allowTaskTools = request.AllowTaskTools
            },
            cancellationToken);
        session = beforeQueryHooks.Session;
        foreach (var hookEvent in beforeQueryHooks.Events)
        {
            yield return hookEvent;
        }

        if (!string.IsNullOrWhiteSpace(beforeQueryHooks.BlockingFailure))
        {
            yield return new TurnFailedStreamEvent(beforeQueryHooks.BlockingFailure);
            yield break;
        }

        yield return new UserTurnAcceptedEvent(session);

        var turnsExecuted = 0;
        string assistantText = string.Empty;

        while (turnsExecuted < request.MaxTurns)
        {
            cancellationToken.ThrowIfCancellationRequested();
            turnsExecuted++;

            var modelRequest = new ModelRequest(
                session.Model,
                DefaultSystemPrompt,
                session.Messages.Select(ToModelMessage).ToArray(),
                _toolRegistry.Tools
                    .Where(tool => request.AllowTaskTools || !IsTaskToolName(tool.Name))
                    .Select(tool => new ToolDefinition(tool.Name, tool.Description, tool.InputSchema))
                    .ToArray());

            var toolCalls = new List<ToolCall>();
            var assistantDraft = string.Empty;
            var currentToolUse = (Id: string.Empty, Name: string.Empty, Input: string.Empty);

            await foreach (var modelEvent in _anthropicMessageClient.StreamAsync(modelRequest, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();

                switch (modelEvent)
                {
                    case MessageStartedEvent messageStarted when !string.IsNullOrWhiteSpace(messageStarted.Model):
                        session = session with { Model = messageStarted.Model };
                        break;
                    case TextDeltaEvent textDelta:
                        assistantDraft += textDelta.Text;
                        yield return new AssistantTextDeltaStreamEvent(textDelta.Text);
                        break;
                    case TextCompletedEvent:
                        if (!string.IsNullOrWhiteSpace(assistantDraft))
                        {
                            assistantText = assistantDraft;
                            var responseTimestamp = DateTimeOffset.UtcNow;
                            workingMessages.Add(new ConversationMessage("assistant", assistantDraft, responseTimestamp));
                            session = session with
                            {
                                UpdatedAtUtc = responseTimestamp,
                                Messages = workingMessages.ToArray()
                            };
                            await _conversationStore.SaveAsync(session, cancellationToken);
                            yield return new AssistantMessageCommittedEvent(session, assistantDraft);
                            assistantDraft = string.Empty;
                        }
                        break;
                    case ToolUseStartedEvent toolUseStarted:
                        currentToolUse = (toolUseStarted.Id, toolUseStarted.Name, string.Empty);
                        yield return new ToolCallRequestedEvent(new ToolCall(toolUseStarted.Id, toolUseStarted.Name, null));
                        break;
                    case ToolUseInputDeltaEvent toolUseInputDelta:
                        currentToolUse.Input += toolUseInputDelta.PartialJson;
                        break;
                    case ToolUseCompletedEvent toolUseCompleted:
                    {
                        var input = toolUseCompleted.Input
                            ?? ParseJsonInput(currentToolUse.Input);
                        var toolCall = new ToolCall(toolUseCompleted.Id, toolUseCompleted.Name, input);
                        toolCalls.Add(toolCall);
                        workingMessages.Add(new ConversationMessage(
                            "tool_use",
                            input?.ToJsonString() ?? "{}",
                            DateTimeOffset.UtcNow,
                            toolCall.Name,
                            toolCall.Id));
                        yield return new ToolCallRequestedEvent(toolCall);
                        currentToolUse = (string.Empty, string.Empty, string.Empty);
                        break;
                    }
                    case ModelErrorEvent errorEvent:
                        yield return new TurnFailedStreamEvent(errorEvent.Message);
                        yield break;
                }
            }

            session = session with
            {
                UpdatedAtUtc = DateTimeOffset.UtcNow,
                Messages = workingMessages.ToArray()
            };

            if (toolCalls.Count == 0)
            {
                await _conversationStore.SaveAsync(session, cancellationToken);
                break;
            }

            foreach (var toolCall in toolCalls)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if ((!request.AllowTaskTools && IsTaskToolName(toolCall.Name)) ||
                    !_toolRegistry.TryGet(toolCall.Name, out var tool) || tool is null)
                {
                    var unknownMessage = new ConversationMessage(
                        "tool_result",
                        $"Unknown tool '{toolCall.Name}'.",
                        DateTimeOffset.UtcNow,
                        toolCall.Name,
                        toolCall.Id,
                        true);
                    workingMessages.Add(unknownMessage);
                    session = session with
                    {
                        UpdatedAtUtc = unknownMessage.TimestampUtc,
                        Messages = workingMessages.ToArray()
                    };
                    await _conversationStore.SaveAsync(session, cancellationToken);
                    yield return new ToolResultCommittedEvent(
                        session,
                        toolCall,
                        new ToolExecutionResult(false, string.Empty, unknownMessage.Content));
                    continue;
                }

                var permissionDecision = _permissionService.Evaluate(tool, request.PermissionMode);
                var permissionMessage = new ConversationMessage(
                    "permission",
                    $"{permissionDecision.Kind}: {permissionDecision.Reason}",
                    DateTimeOffset.UtcNow,
                    toolCall.Name,
                    toolCall.Id,
                    permissionDecision.Kind == PermissionDecisionKind.Deny);
                workingMessages.Add(permissionMessage);
                session = session with
                {
                    UpdatedAtUtc = permissionMessage.TimestampUtc,
                    Messages = workingMessages.ToArray()
                };
                yield return new PermissionDecisionStreamEvent(toolCall, permissionDecision);

                ToolExecutionResult toolResponse;
                if (tool.RequiresEditReview && tool is IReviewableEditTool reviewableEditTool)
                {
                    var preview = await reviewableEditTool.PreviewAsync(
                        new ToolExecutionRequest(toolCall.Name, toolCall.Input, null, session.Id, request.PermissionMode),
                        cancellationToken);
                    var previewMessage = new ConversationMessage(
                        "edit_preview",
                        preview.Success ? preview.Summary : preview.Error ?? "Edit preview failed.",
                        DateTimeOffset.UtcNow,
                        toolCall.Name,
                        toolCall.Id,
                        !preview.Success);
                    workingMessages.Add(previewMessage);
                    session = session with
                    {
                        UpdatedAtUtc = previewMessage.TimestampUtc,
                        Messages = workingMessages.ToArray()
                    };
                    await _conversationStore.SaveAsync(session, cancellationToken);
                    yield return new EditPreviewGeneratedEvent(session, toolCall, preview);

                    if (!preview.Success)
                    {
                        toolResponse = new ToolExecutionResult(false, string.Empty, preview.Error ?? "Edit preview failed.");
                    }
                    else if (permissionDecision.Kind == PermissionDecisionKind.Deny)
                    {
                        var rejectedMessage = new ConversationMessage(
                            "edit_rejected",
                            "Edit batch denied by permission policy.",
                            DateTimeOffset.UtcNow,
                            toolCall.Name,
                            toolCall.Id,
                            true);
                        workingMessages.Add(rejectedMessage);
                        session = session with
                        {
                            UpdatedAtUtc = rejectedMessage.TimestampUtc,
                            Messages = workingMessages.ToArray()
                        };
                        await _conversationStore.SaveAsync(session, cancellationToken);
                        yield return new EditApprovalRecordedEvent(session, toolCall, false, rejectedMessage.Content);
                        toolResponse = new ToolExecutionResult(
                            false,
                            string.Empty,
                            $"Edit batch denied.{Environment.NewLine}{preview.Summary}{Environment.NewLine}{preview.Diff}".TrimEnd());
                    }
                    else if (permissionDecision.Kind == PermissionDecisionKind.Ask)
                    {
                        var approved = request.ApprovalHandler is not null &&
                                       await request.ApprovalHandler.ApproveAsync(tool, toolCall, permissionDecision, cancellationToken);
                        var approvalMessage = new ConversationMessage(
                            approved ? "edit_approved" : "edit_rejected",
                            approved ? "Approved edit batch for application." : "Rejected edit batch.",
                            DateTimeOffset.UtcNow,
                            toolCall.Name,
                            toolCall.Id,
                            !approved);
                        workingMessages.Add(approvalMessage);
                        session = session with
                        {
                            UpdatedAtUtc = approvalMessage.TimestampUtc,
                            Messages = workingMessages.ToArray()
                        };
                        await _conversationStore.SaveAsync(session, cancellationToken);
                        yield return new EditApprovalRecordedEvent(session, toolCall, approved, approvalMessage.Content);

                        if (!approved)
                        {
                            toolResponse = new ToolExecutionResult(
                                false,
                                string.Empty,
                                $"Edit batch rejected.{Environment.NewLine}{preview.Summary}{Environment.NewLine}{preview.Diff}".TrimEnd());
                        }
                        else
                        {
                            var applyResult = await reviewableEditTool.ApplyAsync(
                                new ToolExecutionRequest(toolCall.Name, toolCall.Input, null, session.Id, request.PermissionMode),
                                cancellationToken);
                            toolResponse = applyResult.Success
                                ? new ToolExecutionResult(true, $"{applyResult.Summary}{Environment.NewLine}{applyResult.Diff}".TrimEnd())
                                : new ToolExecutionResult(false, string.Empty, applyResult.Error ?? applyResult.Summary);
                        }
                    }
                    else
                    {
                        var approvalMessage = new ConversationMessage(
                            "edit_approved",
                            "Approved edit batch for application.",
                            DateTimeOffset.UtcNow,
                            toolCall.Name,
                            toolCall.Id,
                            false);
                        workingMessages.Add(approvalMessage);
                        session = session with
                        {
                            UpdatedAtUtc = approvalMessage.TimestampUtc,
                            Messages = workingMessages.ToArray()
                        };
                        await _conversationStore.SaveAsync(session, cancellationToken);
                        yield return new EditApprovalRecordedEvent(session, toolCall, true, approvalMessage.Content);

                        var applyResult = await reviewableEditTool.ApplyAsync(
                            new ToolExecutionRequest(toolCall.Name, toolCall.Input, null, session.Id, request.PermissionMode),
                            cancellationToken);
                        toolResponse = applyResult.Success
                            ? new ToolExecutionResult(true, $"{applyResult.Summary}{Environment.NewLine}{applyResult.Diff}".TrimEnd())
                            : new ToolExecutionResult(false, string.Empty, applyResult.Error ?? applyResult.Summary);
                    }
                }
                else if (permissionDecision.Kind == PermissionDecisionKind.Deny)
                {
                    toolResponse = new ToolExecutionResult(false, string.Empty, $"Permission denied for tool '{toolCall.Name}'.");
                }
                else if (permissionDecision.Kind == PermissionDecisionKind.Ask)
                {
                    var approved = request.ApprovalHandler is not null &&
                                   await request.ApprovalHandler.ApproveAsync(tool, toolCall, permissionDecision, cancellationToken);
                    var approvalMessage = new ConversationMessage(
                        "permission",
                        approved ? "Allow: user approved tool execution." : "Deny: user denied tool execution.",
                        DateTimeOffset.UtcNow,
                        toolCall.Name,
                        toolCall.Id,
                        !approved);
                    workingMessages.Add(approvalMessage);
                    session = session with
                    {
                        UpdatedAtUtc = approvalMessage.TimestampUtc,
                        Messages = workingMessages.ToArray()
                    };
                    yield return new PermissionDecisionStreamEvent(
                        toolCall,
                        approved
                            ? new PermissionDecision(PermissionDecisionKind.Allow, "User approved tool execution.")
                            : new PermissionDecision(PermissionDecisionKind.Deny, "User denied tool execution."));

                    toolResponse = approved
                        ? await _toolExecutor.ExecuteAsync(new ToolExecutionRequest(toolCall.Name, toolCall.Input, null, session.Id, request.PermissionMode), cancellationToken)
                        : new ToolExecutionResult(false, string.Empty, $"Permission denied for tool '{toolCall.Name}'.");
                }
                else
                {
                    toolResponse = await _toolExecutor.ExecuteAsync(
                        new ToolExecutionRequest(toolCall.Name, toolCall.Input, null, session.Id, request.PermissionMode),
                        cancellationToken);
                }

                var toolResultMessage = new ConversationMessage(
                    "tool_result",
                    toolResponse.Success ? toolResponse.Output : toolResponse.Error ?? "Tool failed.",
                    DateTimeOffset.UtcNow,
                    toolCall.Name,
                    toolCall.Id,
                    !toolResponse.Success);
                workingMessages.Add(toolResultMessage);
                session = session with
                {
                    UpdatedAtUtc = toolResultMessage.TimestampUtc,
                    Messages = workingMessages.ToArray()
                };
                await _conversationStore.SaveAsync(session, cancellationToken);
                yield return new ToolResultCommittedEvent(session, toolCall, toolResponse);
                var afterToolHooks = await EmitHookResultsAsync(
                    session,
                    workingMessages,
                    PluginHookKind.AfterToolResult,
                    new
                    {
                        tool = toolCall.Name,
                        toolCallId = toolCall.Id,
                        success = toolResponse.Success,
                        output = toolResponse.Success ? toolResponse.Output : null,
                        error = toolResponse.Success ? null : toolResponse.Error
                    },
                    cancellationToken);
                session = afterToolHooks.Session;
                foreach (var hookEvent in afterToolHooks.Events)
                {
                    yield return hookEvent;
                }

                if (!string.IsNullOrWhiteSpace(afterToolHooks.BlockingFailure))
                {
                    yield return new TurnFailedStreamEvent(afterToolHooks.BlockingFailure);
                    yield break;
                }
                foreach (var taskEvent in ParseTaskStreamEvents(toolCall.Name, toolResponse, request.PermissionMode))
                {
                    yield return taskEvent;
                }
            }

            session = session with
            {
                UpdatedAtUtc = DateTimeOffset.UtcNow,
                Messages = workingMessages.ToArray()
            };
        }

        await _conversationStore.SaveAsync(session, cancellationToken);
        var afterQueryHooks = await EmitHookResultsAsync(
            session,
            workingMessages,
            PluginHookKind.AfterQuery,
            new
            {
                assistantText,
                turnsExecuted,
                messageCount = session.Messages.Count
            },
            cancellationToken);
        session = afterQueryHooks.Session;
        foreach (var hookEvent in afterQueryHooks.Events)
        {
            yield return hookEvent;
        }

        if (!string.IsNullOrWhiteSpace(afterQueryHooks.BlockingFailure))
        {
            yield return new TurnFailedStreamEvent(afterQueryHooks.BlockingFailure);
            yield break;
        }

        yield return new TurnCompletedStreamEvent(new QueryExecutionResult(session, assistantText, turnsExecuted));
    }

    private async Task<HookEmissionResult> EmitHookResultsAsync(
        ConversationSession session,
        List<ConversationMessage> workingMessages,
        PluginHookKind kind,
        object payload,
        CancellationToken cancellationToken)
    {
        var hookResults = await _pluginRuntime.InvokeHooksAsync(
            new PluginHookInvocation(kind, session.Id, null, Environment.CurrentDirectory, payload),
            cancellationToken);
        var events = new List<QueryStreamEvent>();
        string? blockingFailure = null;

        foreach (var hookResult in hookResults)
        {
            var hookMessage = new ConversationMessage(
                hookResult.Success ? "plugin_hook" : "plugin_hook_error",
                hookResult.Message,
                DateTimeOffset.UtcNow,
                $"{hookResult.Plugin.Name}:{hookResult.Hook.Kind}",
                null,
                !hookResult.Success);
            workingMessages.Add(hookMessage);
            session = session with
            {
                UpdatedAtUtc = hookMessage.TimestampUtc,
                Messages = workingMessages.ToArray()
            };
            await _conversationStore.SaveAsync(session, cancellationToken);
            events.Add(new PluginHookRecordedEvent(session, hookResult));

            if (!hookResult.Success && hookResult.Blocking && string.IsNullOrWhiteSpace(blockingFailure))
            {
                blockingFailure = $"Blocking plugin hook failed: {hookResult.Plugin.Name}/{hookResult.Hook.Kind} - {hookResult.Message}";
            }
        }

        return new HookEmissionResult(session, events, blockingFailure);
    }

    private sealed record HookEmissionResult(
        ConversationSession Session,
        IReadOnlyList<QueryStreamEvent> Events,
        string? BlockingFailure);

    private sealed class NoOpPluginRuntime : IPluginRuntime
    {
        public Task ReloadAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<PluginCommandResult?> TryExecuteCommandAsync(CommandRequest request, CancellationToken cancellationToken)
            => Task.FromResult<PluginCommandResult?>(null);

        public Task<IReadOnlyList<PluginHookResult>> InvokeHooksAsync(PluginHookInvocation invocation, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<PluginHookResult>>([]);
    }

    private async Task<ConversationSession> LoadOrCreateSessionAsync(QueryRequest request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.SessionId))
        {
            var existing = await _conversationStore.GetAsync(request.SessionId, cancellationToken);
            if (existing is null)
            {
                throw new ConversationStoreException($"Session '{request.SessionId}' was not found.");
            }

            return string.IsNullOrWhiteSpace(request.Model)
                ? existing
                : existing with { Model = request.Model! };
        }

        var title = request.Prompt.Length > 60 ? $"{request.Prompt[..57]}..." : request.Prompt;
        return await _conversationStore.CreateAsync(title, request.Model ?? DefaultModel, cancellationToken);
    }

    private static ModelMessage ToModelMessage(ConversationMessage message)
    {
        return new ModelMessage(
            message.Role,
            message.Content,
            message.ToolCallId,
            message.ToolName,
            message.IsError);
    }

    private static System.Text.Json.Nodes.JsonNode? ParseJsonInput(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return System.Text.Json.Nodes.JsonNode.Parse(json);
        }
        catch
        {
            return null;
        }
    }

    private static bool IsTaskToolName(string toolName)
    {
        return toolName.StartsWith("task_", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<QueryStreamEvent> ParseTaskStreamEvents(
        string toolName,
        ToolExecutionResult toolResponse,
        PermissionMode permissionMode)
    {
        if (!toolResponse.Success || !IsTaskToolName(toolName) || string.IsNullOrWhiteSpace(toolResponse.Output))
        {
            return [];
        }

        JsonDocument? document = null;
        var events = new List<QueryStreamEvent>();
        try
        {
            document = JsonDocument.Parse(toolResponse.Output);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return [];
            }

            var task = ParseTaskRecord(root, permissionMode);
            var summary = root.TryGetProperty("summary", out var summaryElement)
                ? summaryElement.GetString()
                : root.TryGetProperty("lastStatusMessage", out var lastStatus)
                    ? lastStatus.GetString()
                    : null;

            switch (toolName.ToLowerInvariant())
            {
                case "task_start":
                    events.Add(new TaskStartedStreamEvent(task));
                    break;
                case "task_status":
                    events.Add(new TaskUpdatedStreamEvent(
                        task,
                        new TaskEvent(task.Status, summary ?? task.LastStatusMessage ?? "Task status read.", DateTimeOffset.UtcNow, task.Status == ClawdNet.Core.Models.TaskStatus.Failed)));
                    break;
                case "task_cancel":
                    events.Add(new TaskCanceledStreamEvent(
                        task,
                        new TaskEvent(ClawdNet.Core.Models.TaskStatus.Canceled, summary ?? task.LastStatusMessage ?? "Task canceled.", DateTimeOffset.UtcNow, true)));
                    break;
            }
        }
        catch
        {
            return [];
        }
        finally
        {
            document?.Dispose();
        }

        return events;
    }

    private static TaskRecord ParseTaskRecord(JsonElement root, PermissionMode permissionMode)
    {
        var now = DateTimeOffset.UtcNow;
        var taskId = root.TryGetProperty("taskId", out var taskIdElement)
            ? taskIdElement.GetString() ?? Guid.NewGuid().ToString("N")
            : Guid.NewGuid().ToString("N");
        var title = root.TryGetProperty("title", out var titleElement)
            ? titleElement.GetString() ?? "Background task"
            : "Background task";
        var workerSessionId = root.TryGetProperty("workerSessionId", out var workerElement)
            ? workerElement.GetString() ?? "worker"
            : "worker";
        var status = root.TryGetProperty("status", out var statusElement) &&
                     Enum.TryParse<ClawdNet.Core.Models.TaskStatus>(statusElement.GetString(), true, out var parsedStatus)
            ? parsedStatus
            : ClawdNet.Core.Models.TaskStatus.Running;
        var summary = root.TryGetProperty("summary", out var summaryElement)
            ? summaryElement.GetString()
            : root.TryGetProperty("lastStatusMessage", out var lastStatus)
                ? lastStatus.GetString()
                : null;

        return new TaskRecord(
            taskId,
            TaskKind.Worker,
            title,
            summary ?? title,
            string.Empty,
            workerSessionId,
            root.TryGetProperty("model", out var modelElement) ? modelElement.GetString() ?? DefaultModel : DefaultModel,
            permissionMode,
            status,
            now,
            now,
            status is ClawdNet.Core.Models.TaskStatus.Completed or ClawdNet.Core.Models.TaskStatus.Canceled or ClawdNet.Core.Models.TaskStatus.Failed or ClawdNet.Core.Models.TaskStatus.Interrupted ? now : null,
            null,
            null,
            summary,
            summary is null ? null : new TaskResult(status == ClawdNet.Core.Models.TaskStatus.Completed, summary, status == ClawdNet.Core.Models.TaskStatus.Completed ? null : summary),
            []);
    }
}
