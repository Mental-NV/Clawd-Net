using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Exceptions;
using ClawdNet.Core.Models;
using ClawdNet.Terminal.Abstractions;
using ClawdNet.Terminal.Models;

namespace ClawdNet.Terminal.Repl;

public sealed class ReplHost : IReplHost
{
    private readonly ITerminalSession _terminalSession;
    private readonly IConversationStore _conversationStore;
    private readonly IQueryEngine _queryEngine;
    private readonly ITranscriptRenderer _transcriptRenderer;
    private readonly IToolApprovalHandler _approvalHandler;
    private TerminalActivityState _activityState = TerminalActivityState.Ready;
    private string? _activityDetail;
    private ConversationSession? _currentSession;
    private PermissionMode _currentPermissionMode = PermissionMode.Default;
    private int _visibleStartIndex;

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
        _approvalHandler = new TerminalApprovalHandler(terminalSession, HandleActivityChange);
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

        _currentSession = session;
        _currentPermissionMode = options.PermissionMode;
        _visibleStartIndex = 0;
        var renderedCount = session.Messages.Count;
        Render(session, options.PermissionMode, _visibleStartIndex, clearScreen: true);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var input = await _terminalSession.ReadLineAsync("> ", cancellationToken);
            if (input is null)
            {
                SetActivity(TerminalActivityState.Exiting, "Exiting ClawdNet.");
                Render(session, options.PermissionMode, _visibleStartIndex, clearScreen: true);
                return CommandExecutionResult.Success();
            }

            var prompt = input.Trim();
            if (string.IsNullOrWhiteSpace(prompt))
            {
                continue;
            }

            if (string.Equals(prompt, "exit", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(prompt, "quit", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(prompt, "/exit", StringComparison.OrdinalIgnoreCase))
            {
                SetActivity(TerminalActivityState.Exiting, "Exiting ClawdNet.");
                Render(session, options.PermissionMode, _visibleStartIndex, clearScreen: true);
                return CommandExecutionResult.Success();
            }

            if (TryHandleSlashCommand(prompt, session, options, ref _visibleStartIndex))
            {
                Render(session, options.PermissionMode, _visibleStartIndex, clearScreen: true);
                renderedCount = Math.Max(renderedCount, _visibleStartIndex);
                continue;
            }

            try
            {
                SetActivity(TerminalActivityState.WaitingForModel, "Waiting for model response...");
                Render(session, options.PermissionMode, _visibleStartIndex, clearScreen: true);
                var result = await _queryEngine.AskAsync(
                    new QueryRequest(prompt, session.Id, session.Model, 8, options.PermissionMode, _approvalHandler),
                    cancellationToken);
                session = result.Session;
                _currentSession = session;
                renderedCount = session.Messages.Count;
                SetActivity(TerminalActivityState.Ready, null);
                Render(session, options.PermissionMode, _visibleStartIndex, clearScreen: true);
            }
            catch (AnthropicConfigurationException ex)
            {
                SetActivity(TerminalActivityState.Error, ex.Message);
                Render(session, options.PermissionMode, _visibleStartIndex, clearScreen: true, ex.Message);
                _terminalSession.WriteErrorLine(ex.Message);
            }
            catch (ConversationStoreException ex)
            {
                SetActivity(TerminalActivityState.Error, ex.Message);
                Render(session, options.PermissionMode, _visibleStartIndex, clearScreen: true, ex.Message);
                _terminalSession.WriteErrorLine(ex.Message);
                return CommandExecutionResult.Failure(ex.Message, 3);
            }
        }
    }

    private bool TryHandleSlashCommand(
        string prompt,
        ConversationSession session,
        ReplLaunchOptions options,
        ref int visibleStartIndex)
    {
        switch (prompt)
        {
            case "/help":
                SetActivity(
                    TerminalActivityState.ShowingHelp,
                    "Commands: /help, /session, /clear, /exit. You can also use exit or quit.");
                return true;
            case "/session":
                SetActivity(
                    TerminalActivityState.ShowingSession,
                    $"Session {session.Id} | model={session.Model} | permission={FormatPermissionMode(options.PermissionMode)} | messages={session.Messages.Count}");
                return true;
            case "/clear":
                visibleStartIndex = session.Messages.Count;
                _terminalSession.ClearVisible();
                SetActivity(TerminalActivityState.Cleared, "Screen cleared. Session history is preserved.");
                return true;
            default:
                return false;
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

    private void Render(
        ConversationSession session,
        PermissionMode permissionMode,
        int visibleStartIndex,
        bool clearScreen,
        string? error = null)
    {
        var visibleMessages = session.Messages.Skip(visibleStartIndex).ToArray();
        var transcript = _transcriptRenderer.Render(visibleMessages);
        var footer = _transcriptRenderer.RenderFooter(session, permissionMode, error);
        var activity = _transcriptRenderer.RenderActivity(_activityState, _activityDetail);
        _terminalSession.Render(new TerminalViewState("ClawdNet interactive mode", transcript, footer, activity, clearScreen));
    }

    private void SetActivity(TerminalActivityState state, string? detail)
    {
        _activityState = state;
        _activityDetail = detail;
    }

    private void HandleActivityChange(TerminalActivityState state, string? detail)
    {
        SetActivity(state, detail);
        if (_currentSession is not null)
        {
            Render(_currentSession, _currentPermissionMode, _visibleStartIndex, clearScreen: true);
        }
    }

    private static string FormatPermissionMode(PermissionMode permissionMode)
    {
        return permissionMode switch
        {
            PermissionMode.Default => "default",
            PermissionMode.AcceptEdits => "accept-edits",
            PermissionMode.BypassPermissions => "bypass-permissions",
            _ => permissionMode.ToString()
        };
    }
}
