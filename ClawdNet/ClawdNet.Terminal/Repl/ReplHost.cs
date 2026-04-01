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
    private readonly IPtyManager _ptyManager;
    private readonly ITaskManager _taskManager;
    private readonly PromptHistoryBuffer _promptHistory = new();
    private TerminalActivityState _activityState = TerminalActivityState.Ready;
    private string? _activityDetail;
    private ConversationSession? _currentSession;
    private PermissionMode _currentPermissionMode = PermissionMode.Default;
    private int _visibleStartIndex;
    private StreamingAssistantDraft? _draft;
    private TerminalViewportState _viewport = new();
    private string _promptBuffer = string.Empty;
    private CancellationTokenSource? _activeTurnCancellation;

    public ReplHost(
        ITerminalSession terminalSession,
        IConversationStore conversationStore,
        IQueryEngine queryEngine,
        ITranscriptRenderer transcriptRenderer,
        IPtyManager ptyManager,
        ITaskManager taskManager)
    {
        _terminalSession = terminalSession;
        _conversationStore = conversationStore;
        _queryEngine = queryEngine;
        _transcriptRenderer = transcriptRenderer;
        _ptyManager = ptyManager;
        _taskManager = taskManager;
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
        _viewport = new TerminalViewportState();
        _promptBuffer = string.Empty;
        _promptHistory.ResetNavigation();
        _ptyManager.SessionChanged += HandlePtySessionChanged;
        _taskManager.TaskChanged += HandleTaskChanged;
        try
        {
            Render(session, options.PermissionMode, _visibleStartIndex, clearScreen: true);

            using var interruptRegistration = _terminalSession.RegisterInterruptHandler(() =>
            {
                if (_ptyManager.CurrentState?.IsRunning == true)
                {
                    _ = _ptyManager.CloseAsync(CancellationToken.None);
                    return;
                }

                if (_activeTurnCancellation is null)
                {
                    return;
                }

                SetActivity(TerminalActivityState.Interrupted, "Interrupted active turn.");
                _draft = null;
                _activeTurnCancellation.Cancel();
                if (_currentSession is not null)
                {
                    Render(_currentSession, _currentPermissionMode, _visibleStartIndex, clearScreen: true);
                }
            });

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var promptResult = await _terminalSession.ReadPromptAsync("> ", _promptBuffer, cancellationToken);
                switch (promptResult.Kind)
                {
                    case PromptInputKind.EndOfStream:
                        SetActivity(TerminalActivityState.Exiting, "Exiting ClawdNet.");
                        Render(session, options.PermissionMode, _visibleStartIndex, clearScreen: true);
                        return CommandExecutionResult.Success();
                    case PromptInputKind.BufferChanged:
                        _promptBuffer = promptResult.Text ?? string.Empty;
                        Render(session, options.PermissionMode, _visibleStartIndex, clearScreen: true);
                        continue;
                    case PromptInputKind.HistoryPrevious:
                        _promptBuffer = _promptHistory.Previous(_promptBuffer);
                        Render(session, options.PermissionMode, _visibleStartIndex, clearScreen: true);
                        continue;
                    case PromptInputKind.HistoryNext:
                        _promptBuffer = _promptHistory.Next();
                        Render(session, options.PermissionMode, _visibleStartIndex, clearScreen: true);
                        continue;
                    case PromptInputKind.ScrollPageUp:
                        ScrollPageUp(session.Messages.Count - _visibleStartIndex);
                        Render(session, options.PermissionMode, _visibleStartIndex, clearScreen: true);
                        continue;
                    case PromptInputKind.ScrollPageDown:
                        ScrollPageDown();
                        Render(session, options.PermissionMode, _visibleStartIndex, clearScreen: true);
                        continue;
                    case PromptInputKind.ScrollBottom:
                        ScrollBottom();
                        Render(session, options.PermissionMode, _visibleStartIndex, clearScreen: true);
                        continue;
                }

                var rawPrompt = promptResult.Text ?? _promptBuffer;
                _promptBuffer = rawPrompt;
                var prompt = rawPrompt.Trim();
                if (string.IsNullOrWhiteSpace(prompt))
                {
                    _promptBuffer = string.Empty;
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

                if (await TryHandleSlashCommandAsync(prompt, session, options, cancellationToken))
                {
                    _promptBuffer = string.Empty;
                    _promptHistory.ResetNavigation();
                    Render(session, options.PermissionMode, _visibleStartIndex, clearScreen: true);
                    continue;
                }

                _promptHistory.Add(prompt);
                _promptBuffer = string.Empty;
                try
                {
                    SetActivity(TerminalActivityState.WaitingForModel, "Waiting for model response...");
                    _draft = new StreamingAssistantDraft(string.Empty, true, null, "Submitting prompt...");
                    Render(session, options.PermissionMode, _visibleStartIndex, clearScreen: true);
                    QueryExecutionResult? result = null;
                    using var turnCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    _activeTurnCancellation = turnCancellation;

                    await foreach (var streamEvent in _queryEngine.StreamAskAsync(
                                       new QueryRequest(prompt, session.Id, session.Model, 8, options.PermissionMode, _approvalHandler),
                                       turnCancellation.Token))
                    {
                        switch (streamEvent)
                        {
                            case UserTurnAcceptedEvent accepted:
                                session = accepted.Session;
                                _currentSession = session;
                                MarkLiveUpdate();
                                SetActivity(TerminalActivityState.WaitingForModel, "Waiting for model response...");
                                _draft = new StreamingAssistantDraft(string.Empty, true, null, "Waiting for model response...");
                                Render(session, options.PermissionMode, _visibleStartIndex, clearScreen: true);
                                break;
                            case AssistantTextDeltaStreamEvent delta:
                                MarkLiveUpdate();
                                var currentText = _draft?.Text ?? string.Empty;
                                _draft = new StreamingAssistantDraft(
                                    $"{currentText}{delta.DeltaText}",
                                    true,
                                    null,
                                    "Streaming assistant response...");
                                SetActivity(TerminalActivityState.StreamingResponse, "Streaming assistant response...");
                                Render(session, options.PermissionMode, _visibleStartIndex, clearScreen: true);
                                break;
                            case AssistantMessageCommittedEvent committed:
                                session = committed.Session;
                                _currentSession = session;
                                MarkLiveUpdate();
                                _draft = null;
                                SetActivity(TerminalActivityState.Ready, null);
                                Render(session, options.PermissionMode, _visibleStartIndex, clearScreen: true);
                                break;
                            case ToolCallRequestedEvent toolCallRequested:
                                MarkLiveUpdate();
                                _draft = new StreamingAssistantDraft(
                                    string.Empty,
                                    true,
                                    toolCallRequested.ToolCall.Name,
                                    $"Preparing tool {toolCallRequested.ToolCall.Name}...");
                                SetActivity(TerminalActivityState.RunningTool, $"Preparing tool {toolCallRequested.ToolCall.Name}...");
                                Render(session, options.PermissionMode, _visibleStartIndex, clearScreen: true);
                                break;
                            case PermissionDecisionStreamEvent permissionEvent:
                                MarkLiveUpdate();
                                _draft = new StreamingAssistantDraft(
                                    string.Empty,
                                    true,
                                    permissionEvent.ToolCall.Name,
                                    permissionEvent.Decision.Kind == PermissionDecisionKind.Ask
                                        ? "Awaiting approval..."
                                        : permissionEvent.Decision.Reason);
                                SetActivity(
                                    permissionEvent.Decision.Kind == PermissionDecisionKind.Ask
                                        ? TerminalActivityState.AwaitingApproval
                                        : TerminalActivityState.RunningTool,
                                    permissionEvent.Decision.Reason);
                                Render(session, options.PermissionMode, _visibleStartIndex, clearScreen: true);
                                break;
                            case EditPreviewGeneratedEvent previewEvent:
                                session = previewEvent.Session;
                                _currentSession = session;
                                MarkLiveUpdate();
                                _draft = new StreamingAssistantDraft(
                                    string.Empty,
                                    true,
                                    previewEvent.ToolCall.Name,
                                    previewEvent.Preview.Diff);
                                SetActivity(
                                    TerminalActivityState.ReviewingEdits,
                                    previewEvent.Preview.Success
                                        ? previewEvent.Preview.Summary
                                        : previewEvent.Preview.Error ?? "Edit preview failed.");
                                Render(session, options.PermissionMode, _visibleStartIndex, clearScreen: true);
                                break;
                            case EditApprovalRecordedEvent editApproval:
                                session = editApproval.Session;
                                _currentSession = session;
                                MarkLiveUpdate();
                                _draft = editApproval.Approved
                                    ? new StreamingAssistantDraft(string.Empty, true, editApproval.ToolCall.Name, "Applying approved edit batch...")
                                    : null;
                                SetActivity(
                                    editApproval.Approved ? TerminalActivityState.RunningTool : TerminalActivityState.ReviewingEdits,
                                    editApproval.Summary);
                                Render(session, options.PermissionMode, _visibleStartIndex, clearScreen: true);
                                break;
                            case ToolResultCommittedEvent committedTool:
                                session = committedTool.Session;
                                _currentSession = session;
                                MarkLiveUpdate();
                                _draft = new StreamingAssistantDraft(
                                    string.Empty,
                                    true,
                                    committedTool.ToolCall.Name,
                                    committedTool.Result.Success ? "Tool completed." : "Tool failed.");
                                SetActivity(
                                    TerminalActivityState.RunningTool,
                                    committedTool.Result.Success
                                        ? $"Tool {committedTool.ToolCall.Name} completed."
                                        : $"Tool {committedTool.ToolCall.Name} failed.");
                                Render(session, options.PermissionMode, _visibleStartIndex, clearScreen: true);
                                break;
                            case TaskStartedStreamEvent taskStarted:
                                MarkLiveUpdate();
                                SetActivity(TerminalActivityState.RunningTool, $"Task {taskStarted.Task.Id} started: {taskStarted.Task.Title}");
                                Render(session, options.PermissionMode, _visibleStartIndex, clearScreen: true);
                                break;
                            case TaskUpdatedStreamEvent taskUpdated:
                                MarkLiveUpdate();
                                SetActivity(TerminalActivityState.RunningTool, $"Task {taskUpdated.Task.Id}: {taskUpdated.Event.Message}");
                                Render(session, options.PermissionMode, _visibleStartIndex, clearScreen: true);
                                break;
                            case TaskCompletedStreamEvent taskCompleted:
                                MarkLiveUpdate();
                                SetActivity(TerminalActivityState.RunningTool, $"Task {taskCompleted.Task.Id} completed: {taskCompleted.Result.Summary}");
                                Render(session, options.PermissionMode, _visibleStartIndex, clearScreen: true);
                                break;
                            case TaskFailedStreamEvent taskFailed:
                                MarkLiveUpdate();
                                SetActivity(TerminalActivityState.Error, $"Task {taskFailed.Task.Id} failed: {taskFailed.Result.Error ?? taskFailed.Result.Summary}");
                                Render(session, options.PermissionMode, _visibleStartIndex, clearScreen: true);
                                break;
                            case TaskCanceledStreamEvent taskCanceled:
                                MarkLiveUpdate();
                                SetActivity(TerminalActivityState.Interrupted, $"Task {taskCanceled.Task.Id} canceled: {taskCanceled.Event.Message}");
                                Render(session, options.PermissionMode, _visibleStartIndex, clearScreen: true);
                                break;
                            case TurnCompletedStreamEvent completed:
                                result = completed.Result;
                                session = completed.Result.Session;
                                _currentSession = session;
                                MarkLiveUpdate();
                                _draft = null;
                                SetActivity(TerminalActivityState.Ready, null);
                                Render(session, options.PermissionMode, _visibleStartIndex, clearScreen: true);
                                break;
                            case TurnFailedStreamEvent failed:
                                _draft = null;
                                SetActivity(TerminalActivityState.Error, failed.Message);
                                Render(session, options.PermissionMode, _visibleStartIndex, clearScreen: true, failed.Message);
                                _terminalSession.WriteErrorLine(failed.Message);
                                break;
                        }
                    }

                    _activeTurnCancellation = null;
                    if (result is null)
                    {
                        SetActivity(TerminalActivityState.Ready, null);
                    }

                    SetActivity(TerminalActivityState.Ready, null);
                    _draft = null;
                    Render(session, options.PermissionMode, _visibleStartIndex, clearScreen: true);
                }
                catch (OperationCanceledException) when (_activeTurnCancellation?.IsCancellationRequested == true && !cancellationToken.IsCancellationRequested)
                {
                    _activeTurnCancellation = null;
                    _draft = null;
                    SetActivity(TerminalActivityState.Interrupted, "Interrupted active turn.");
                    Render(session, options.PermissionMode, _visibleStartIndex, clearScreen: true);
                }
                catch (AnthropicConfigurationException ex)
                {
                    _activeTurnCancellation = null;
                    _draft = null;
                    SetActivity(TerminalActivityState.Error, ex.Message);
                    Render(session, options.PermissionMode, _visibleStartIndex, clearScreen: true, ex.Message);
                    _terminalSession.WriteErrorLine(ex.Message);
                }
                catch (ConversationStoreException ex)
                {
                    _activeTurnCancellation = null;
                    _draft = null;
                    SetActivity(TerminalActivityState.Error, ex.Message);
                    Render(session, options.PermissionMode, _visibleStartIndex, clearScreen: true, ex.Message);
                    _terminalSession.WriteErrorLine(ex.Message);
                    return CommandExecutionResult.Failure(ex.Message, 3);
                }
            }
        }
        finally
        {
            _ptyManager.SessionChanged -= HandlePtySessionChanged;
            _taskManager.TaskChanged -= HandleTaskChanged;
        }
    }

    private async Task<bool> TryHandleSlashCommandAsync(
        string prompt,
        ConversationSession session,
        ReplLaunchOptions options,
        CancellationToken cancellationToken)
    {
        switch (prompt)
        {
            case "/help":
                SetActivity(
                    TerminalActivityState.ShowingHelp,
                    "Commands: /help, /session, /tasks, /pty, /clear, /bottom, /exit. Keys: Up/Down history, PgUp/PgDn scroll, End bottom.");
                return true;
            case "/session":
                SetActivity(
                    TerminalActivityState.ShowingSession,
                    $"Session {session.Id} | model={session.Model} | permission={FormatPermissionMode(options.PermissionMode)} | messages={session.Messages.Count}");
                return true;
            case "/clear":
                _visibleStartIndex = session.Messages.Count;
                _terminalSession.ClearVisible();
                _viewport = new TerminalViewportState();
                SetActivity(TerminalActivityState.Cleared, "Screen cleared. Session history is preserved.");
                return true;
            case "/pty":
                var ptyState = _ptyManager.CurrentState;
                SetActivity(
                    TerminalActivityState.ShowingSession,
                    ptyState is null
                        ? "No active PTY session."
                        : $"PTY {ptyState.SessionId} | running={ptyState.IsRunning} | command={ptyState.Command}");
                return true;
            case "/tasks":
                var tasks = await _taskManager.ListAsync(cancellationToken);
                var taskSummary = tasks.Count == 0
                    ? "No tasks found."
                    : string.Join(
                        Environment.NewLine,
                        tasks.Take(5).Select(task => $"{task.Id} | {task.Status} | {task.Title}"));
                SetActivity(TerminalActivityState.ShowingSession, taskSummary);
                return true;
            case "/bottom":
                ScrollBottom();
                SetActivity(TerminalActivityState.Ready, "Returned to live output.");
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
        var visibleMessages = GetViewportMessages(session, visibleStartIndex);
        var transcript = _transcriptRenderer.Render(visibleMessages);
        var footer = _transcriptRenderer.RenderFooter(
            session,
            permissionMode,
            _ptyManager.CurrentState,
            _viewport.FollowLiveOutput,
            _viewport.HasBufferedLiveOutput,
            error);
        var draft = _transcriptRenderer.RenderDraft(_draft);
        var pty = _transcriptRenderer.RenderPty(_ptyManager.CurrentState);
        var activity = _transcriptRenderer.RenderActivity(_activityState, _activityDetail);
        _terminalSession.Render(new TerminalViewState("ClawdNet interactive mode", transcript, footer, _promptBuffer, _viewport, draft, pty, activity, clearScreen));
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

    private void HandlePtySessionChanged(PtySessionState? state)
    {
        if (_currentSession is null)
        {
            return;
        }

        MarkLiveUpdate();
        if (state is not null && state.IsRunning)
        {
            SetActivity(TerminalActivityState.RunningTool, $"PTY session active: {state.Command}");
        }

        Render(_currentSession, _currentPermissionMode, _visibleStartIndex, clearScreen: true);
    }

    private async void HandleTaskChanged(TaskRecord task, TaskEvent taskEvent)
    {
        try
        {
            if (_currentSession is null || !string.Equals(_currentSession.Id, task.ParentSessionId, StringComparison.Ordinal))
            {
                return;
            }

            var reloaded = await _conversationStore.GetAsync(task.ParentSessionId, CancellationToken.None);
            if (reloaded is not null)
            {
                _currentSession = reloaded;
            }

            MarkLiveUpdate();
            SetActivity(TerminalActivityState.RunningTool, $"Task {task.Id} | {task.Status} | {taskEvent.Message}");
            if (_currentSession is not null)
            {
                Render(_currentSession, _currentPermissionMode, _visibleStartIndex, clearScreen: true);
            }
        }
        catch
        {
            // ignore event handler failures
        }
    }

    private IReadOnlyList<ConversationMessage> GetViewportMessages(ConversationSession session, int visibleStartIndex)
    {
        var messages = session.Messages.Skip(visibleStartIndex).ToArray();
        if (messages.Length <= _viewport.PageSize)
        {
            return messages;
        }

        var clampedOffset = Math.Clamp(_viewport.ScrollOffsetFromBottom, 0, Math.Max(0, messages.Length - _viewport.PageSize));
        var endExclusive = Math.Max(0, messages.Length - clampedOffset);
        var start = Math.Max(0, endExclusive - _viewport.PageSize);
        return messages.Skip(start).Take(endExclusive - start).ToArray();
    }

    private void ScrollPageUp(int messageCount)
    {
        if (messageCount <= _viewport.PageSize)
        {
            return;
        }

        var maxOffset = Math.Max(0, messageCount - _viewport.PageSize);
        var newOffset = Math.Min(maxOffset, _viewport.ScrollOffsetFromBottom + _viewport.PageSize);
        _viewport = _viewport with
        {
            ScrollOffsetFromBottom = newOffset,
            FollowLiveOutput = newOffset == 0,
            HasBufferedLiveOutput = newOffset == 0 ? false : _viewport.HasBufferedLiveOutput
        };
    }

    private void ScrollPageDown()
    {
        var newOffset = Math.Max(0, _viewport.ScrollOffsetFromBottom - _viewport.PageSize);
        _viewport = _viewport with
        {
            ScrollOffsetFromBottom = newOffset,
            FollowLiveOutput = newOffset == 0,
            HasBufferedLiveOutput = newOffset == 0 ? false : _viewport.HasBufferedLiveOutput
        };
    }

    private void ScrollBottom()
    {
        _viewport = _viewport with
        {
            ScrollOffsetFromBottom = 0,
            FollowLiveOutput = true,
            HasBufferedLiveOutput = false
        };
    }

    private void MarkLiveUpdate()
    {
        if (_viewport.FollowLiveOutput)
        {
            _viewport = _viewport with { HasBufferedLiveOutput = false };
            return;
        }

        _viewport = _viewport with { HasBufferedLiveOutput = true };
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
