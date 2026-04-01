using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Exceptions;
using ClawdNet.Core.Models;

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

    public QueryEngine(
        IConversationStore conversationStore,
        IAnthropicMessageClient anthropicMessageClient,
        IToolRegistry toolRegistry,
        IToolExecutor toolExecutor,
        IPermissionService permissionService)
    {
        _conversationStore = conversationStore;
        _anthropicMessageClient = anthropicMessageClient;
        _toolRegistry = toolRegistry;
        _toolExecutor = toolExecutor;
        _permissionService = permissionService;
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
                _toolRegistry.Tools.Select(tool => new ToolDefinition(tool.Name, tool.Description, tool.InputSchema)).ToArray());

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
                yield return new TurnCompletedStreamEvent(new QueryExecutionResult(session, assistantText, turnsExecuted));
                yield break;
            }

            foreach (var toolCall in toolCalls)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!_toolRegistry.TryGet(toolCall.Name, out var tool) || tool is null)
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
                if (permissionDecision.Kind == PermissionDecisionKind.Deny)
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
                        ? await _toolExecutor.ExecuteAsync(new ToolExecutionRequest(toolCall.Name, toolCall.Input), cancellationToken)
                        : new ToolExecutionResult(false, string.Empty, $"Permission denied for tool '{toolCall.Name}'.");
                }
                else
                {
                    toolResponse = await _toolExecutor.ExecuteAsync(
                        new ToolExecutionRequest(toolCall.Name, toolCall.Input),
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
            }

            session = session with
            {
                UpdatedAtUtc = DateTimeOffset.UtcNow,
                Messages = workingMessages.ToArray()
            };
        }

        await _conversationStore.SaveAsync(session, cancellationToken);
        yield return new TurnCompletedStreamEvent(new QueryExecutionResult(session, assistantText, turnsExecuted));
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
}
