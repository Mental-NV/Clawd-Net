using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Exceptions;
using ClawdNet.Core.Models;
using ClawdNet.Terminal.Abstractions;

namespace ClawdNet.Terminal.Repl;

public sealed class ReplHost : IReplHost
{
    private readonly ITerminalSession _terminalSession;
    private readonly IConversationStore _conversationStore;
    private readonly IQueryEngine _queryEngine;
    private readonly ITranscriptRenderer _transcriptRenderer;
    private readonly IToolApprovalHandler _approvalHandler;

    public ReplHost(
        ITerminalSession terminalSession,
        IConversationStore conversationStore,
        IQueryEngine queryEngine,
        ITranscriptRenderer transcriptRenderer)
    {
        _terminalSession = terminalSession;
        _conversationStore = conversationStore;
        _queryEngine = queryEngine;
        _transcriptRenderer = transcriptRenderer;
        _approvalHandler = new TerminalApprovalHandler(terminalSession);
    }

    public async Task<CommandExecutionResult> RunAsync(ReplLaunchOptions options, CancellationToken cancellationToken)
    {
        ConversationSession session;
        try
        {
            session = await LoadOrCreateSessionAsync(options, cancellationToken);
        }
        catch (ConversationStoreException ex)
        {
            return CommandExecutionResult.Failure(ex.Message, 3);
        }

        _terminalSession.WriteLine("ClawdNet interactive mode");
        _terminalSession.WriteStatus(_transcriptRenderer.RenderStatus(session));

        if (session.Messages.Count > 0)
        {
            _terminalSession.WriteLine(_transcriptRenderer.Render(session.Messages));
        }

        var renderedCount = session.Messages.Count;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var input = await _terminalSession.ReadLineAsync("> ", cancellationToken);
            if (input is null)
            {
                _terminalSession.WriteStatus("Exiting ClawdNet.");
                return CommandExecutionResult.Success();
            }

            var prompt = input.Trim();
            if (string.IsNullOrWhiteSpace(prompt))
            {
                continue;
            }

            if (string.Equals(prompt, "exit", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(prompt, "quit", StringComparison.OrdinalIgnoreCase))
            {
                _terminalSession.WriteStatus("Exiting ClawdNet.");
                return CommandExecutionResult.Success();
            }

            try
            {
                var result = await _queryEngine.AskAsync(
                    new QueryRequest(prompt, session.Id, session.Model, 8, options.PermissionMode, _approvalHandler),
                    cancellationToken);
                session = result.Session;
                var delta = session.Messages.Skip(renderedCount).ToArray();
                if (delta.Length > 0)
                {
                    _terminalSession.WriteLine(_transcriptRenderer.Render(delta));
                    renderedCount = session.Messages.Count;
                }

                _terminalSession.WriteStatus(_transcriptRenderer.RenderStatus(session));
            }
            catch (AnthropicConfigurationException ex)
            {
                _terminalSession.WriteErrorLine(ex.Message);
                _terminalSession.WriteStatus(_transcriptRenderer.RenderStatus(session, ex.Message));
            }
            catch (ConversationStoreException ex)
            {
                _terminalSession.WriteErrorLine(ex.Message);
                return CommandExecutionResult.Failure(ex.Message, 3);
            }
        }
    }

    private async Task<ConversationSession> LoadOrCreateSessionAsync(ReplLaunchOptions options, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(options.SessionId))
        {
            var existing = await _conversationStore.GetAsync(options.SessionId, cancellationToken);
            if (existing is null)
            {
                throw new ConversationStoreException($"Session '{options.SessionId}' was not found.");
            }

            return existing;
        }

        return await _conversationStore.CreateAsync("Interactive session", options.Model ?? "claude-sonnet-4-5", cancellationToken);
    }
}
