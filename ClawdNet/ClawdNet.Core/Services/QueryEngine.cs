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

    public QueryEngine(
        IConversationStore conversationStore,
        IAnthropicMessageClient anthropicMessageClient,
        IToolRegistry toolRegistry,
        IToolExecutor toolExecutor)
    {
        _conversationStore = conversationStore;
        _anthropicMessageClient = anthropicMessageClient;
        _toolRegistry = toolRegistry;
        _toolExecutor = toolExecutor;
    }

    public async Task<QueryExecutionResult> AskAsync(QueryRequest request, CancellationToken cancellationToken)
    {
        var session = await LoadOrCreateSessionAsync(request, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var workingMessages = session.Messages.ToList();
        workingMessages.Add(new ConversationMessage("user", request.Prompt, now));
        session = session with
        {
            UpdatedAtUtc = now,
            Messages = workingMessages
        };

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

            var modelResponse = await _anthropicMessageClient.SendAsync(modelRequest, cancellationToken);

            var responseTimestamp = DateTimeOffset.UtcNow;
            var toolCalls = new List<ToolCall>();

            foreach (var block in modelResponse.ContentBlocks)
            {
                switch (block)
                {
                    case TextContentBlock textBlock:
                        assistantText = textBlock.Text;
                        workingMessages.Add(new ConversationMessage("assistant", textBlock.Text, responseTimestamp));
                        break;
                    case ToolUseContentBlock toolBlock:
                        toolCalls.Add(new ToolCall(toolBlock.Id, toolBlock.Name, toolBlock.Input));
                        workingMessages.Add(new ConversationMessage(
                            "tool_use",
                            toolBlock.Input?.ToJsonString() ?? "{}",
                            responseTimestamp,
                            toolBlock.Name,
                            toolBlock.Id));
                        break;
                }
            }

            session = session with
            {
                Model = modelResponse.Model,
                UpdatedAtUtc = responseTimestamp,
                Messages = workingMessages.ToArray()
            };

            if (toolCalls.Count == 0)
            {
                await _conversationStore.SaveAsync(session, cancellationToken);
                return new QueryExecutionResult(session, assistantText, turnsExecuted);
            }

            foreach (var toolCall in toolCalls)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var toolResponse = await _toolExecutor.ExecuteAsync(
                    new ToolExecutionRequest(toolCall.Name, toolCall.Input),
                    cancellationToken);

                workingMessages.Add(new ConversationMessage(
                    "tool_result",
                    toolResponse.Success ? toolResponse.Output : toolResponse.Error ?? "Tool failed.",
                    DateTimeOffset.UtcNow,
                    toolCall.Name,
                    toolCall.Id,
                    !toolResponse.Success));
            }

            session = session with
            {
                UpdatedAtUtc = DateTimeOffset.UtcNow,
                Messages = workingMessages.ToArray()
            };
        }

        await _conversationStore.SaveAsync(session, cancellationToken);
        return new QueryExecutionResult(session, assistantText, turnsExecuted);
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
}
