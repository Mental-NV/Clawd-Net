using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Exceptions;
using ClawdNet.Core.Models;
using ClawdNet.Terminal.Abstractions;
using ClawdNet.Terminal.Models;

namespace ClawdNet.Terminal.Tui;

public sealed class TuiHost : ITuiHost
{
    private readonly ITerminalSession _terminalSession;
    private readonly IConversationStore _conversationStore;
    private readonly IQueryEngine _queryEngine;
    private readonly ITuiRenderer _tuiRenderer;
    private readonly IPtyManager _ptyManager;
    private readonly ITaskManager _taskManager;
    private readonly PromptHistoryBuffer _promptHistory = new();

    private ConversationSession? _currentSession;
    private PermissionMode _currentPermissionMode = PermissionMode.Default;
    private TerminalViewportState _transcriptViewport = new(PageSize: 18);
    private TerminalViewportState _contextViewport = new(PageSize: 10);
    private StreamingAssistantDraft? _draft;
    private string _promptBuffer = string.Empty;
    private TuiFocusTarget _focus = TuiFocusTarget.Composer;
    private TuiOverlayState? _overlay;
    private TerminalActivityState _activityState = TerminalActivityState.Ready;
    private string? _activityDetail;
    private string? _error;
    private CancellationTokenSource? _activeTurnCancellation;

    public TuiHost(
        ITerminalSession terminalSession,
        IConversationStore conversationStore,
        IQueryEngine queryEngine,
        ITuiRenderer tuiRenderer,
        IPtyManager ptyManager,
        ITaskManager taskManager)
    {
        _terminalSession = terminalSession;
        _conversationStore = conversationStore;
        _queryEngine = queryEngine;
        _tuiRenderer = tuiRenderer;
        _ptyManager = ptyManager;
        _taskManager = taskManager;
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
        _promptBuffer = string.Empty;
        _focus = TuiFocusTarget.Composer;
        _overlay = null;
        _draft = null;
        _activityState = TerminalActivityState.Ready;
        _activityDetail = null;
        _error = null;
        _promptHistory.ResetNavigation();
        _transcriptViewport = new TerminalViewportState(PageSize: 18);
        _contextViewport = new TerminalViewportState(PageSize: 10);

        _ptyManager.StateChanged += HandlePtyStateChanged;
        _taskManager.TaskChanged += HandleTaskChanged;
        _terminalSession.EnterAlternateScreen();

        try
        {
            Render(clearScreen: true);

            using var interruptRegistration = _terminalSession.RegisterInterruptHandler(() =>
            {
                if (_ptyManager.State.CurrentSession?.IsRunning == true)
                {
                    _ = _ptyManager.CloseAsync(null, CancellationToken.None);
                    return;
                }

                if (_activeTurnCancellation is null)
                {
                    return;
                }

                _draft = null;
                _activityState = TerminalActivityState.Interrupted;
                _activityDetail = "Interrupted active turn.";
                _activeTurnCancellation.Cancel();
                Render(clearScreen: true);
            });

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var promptResult = await _terminalSession.ReadPromptAsync("> ", _promptBuffer, cancellationToken);
                if (promptResult.Kind == PromptInputKind.EndOfStream)
                {
                    _activityState = TerminalActivityState.Exiting;
                    _activityDetail = "Exiting ClawdNet.";
                    Render(clearScreen: true);
                    return CommandExecutionResult.Success();
                }

                if (await TryHandleInputAsync(promptResult, options, cancellationToken))
                {
                    if (_currentSession is not null)
                    {
                        session = _currentSession;
                    }
                    continue;
                }

                var prompt = (promptResult.Text ?? _promptBuffer).Trim();
                if (string.IsNullOrWhiteSpace(prompt))
                {
                    _promptBuffer = string.Empty;
                    Render(clearScreen: true);
                    continue;
                }

                if (string.Equals(prompt, "exit", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(prompt, "quit", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(prompt, "/exit", StringComparison.OrdinalIgnoreCase))
                {
                    _activityState = TerminalActivityState.Exiting;
                    _activityDetail = "Exiting ClawdNet.";
                    Render(clearScreen: true);
                    return CommandExecutionResult.Success();
                }

                if (await TryHandleSlashCommandAsync(prompt, cancellationToken))
                {
                    _promptBuffer = string.Empty;
                    _promptHistory.ResetNavigation();
                    if (_currentSession is not null)
                    {
                        session = _currentSession;
                    }
                    Render(clearScreen: true);
                    continue;
                }

                _promptHistory.Add(prompt);
                _promptBuffer = string.Empty;
                _overlay = null;

                try
                {
                    _activityState = TerminalActivityState.WaitingForModel;
                    _activityDetail = "Waiting for model response...";
                    _draft = new StreamingAssistantDraft(string.Empty, true, null, "Submitting prompt...");
                    Render(clearScreen: true);
                    using var turnCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    _activeTurnCancellation = turnCancellation;
                    await foreach (var streamEvent in _queryEngine.StreamAskAsync(
                                       new QueryRequest(prompt, session.Id, session.Model, 8, options.PermissionMode, new TuiApprovalHandler(this)),
                                       turnCancellation.Token))
                    {
                        ApplyStreamEvent(streamEvent);
                        if (_currentSession is not null)
                        {
                            session = _currentSession;
                        }
                        Render(clearScreen: true);
                    }

                    _activeTurnCancellation = null;
                    _draft = null;
                    _activityState = TerminalActivityState.Ready;
                    _activityDetail = null;
                    Render(clearScreen: true);
                }
                catch (OperationCanceledException) when (_activeTurnCancellation?.IsCancellationRequested == true && !cancellationToken.IsCancellationRequested)
                {
                    _activeTurnCancellation = null;
                    _draft = null;
                    _activityState = TerminalActivityState.Interrupted;
                    _activityDetail = "Interrupted active turn.";
                    Render(clearScreen: true);
                }
                catch (AnthropicConfigurationException ex)
                {
                    _activeTurnCancellation = null;
                    _draft = null;
                    _activityState = TerminalActivityState.Error;
                    _activityDetail = ex.Message;
                    _error = ex.Message;
                    _overlay = new TuiOverlayState(TuiOverlayKind.Error, "Configuration error", ex.Message);
                    Render(clearScreen: true);
                    _terminalSession.WriteErrorLine(ex.Message);
                }
                catch (ConversationStoreException ex)
                {
                    _activeTurnCancellation = null;
                    _draft = null;
                    _activityState = TerminalActivityState.Error;
                    _activityDetail = ex.Message;
                    _error = ex.Message;
                    _overlay = new TuiOverlayState(TuiOverlayKind.Error, "Conversation error", ex.Message);
                    Render(clearScreen: true);
                    _terminalSession.WriteErrorLine(ex.Message);
                    return CommandExecutionResult.Failure(ex.Message, 3);
                }
            }
        }
        finally
        {
            _terminalSession.LeaveAlternateScreen();
            _ptyManager.StateChanged -= HandlePtyStateChanged;
            _taskManager.TaskChanged -= HandleTaskChanged;
        }
    }

    private async Task<bool> TryHandleInputAsync(PromptInputResult promptResult, ReplLaunchOptions options, CancellationToken cancellationToken)
    {
        switch (promptResult.Kind)
        {
            case PromptInputKind.BufferChanged:
                _promptBuffer = promptResult.Text ?? string.Empty;
                Render(clearScreen: true);
                return true;
            case PromptInputKind.InsertLineBreak:
                _promptBuffer = promptResult.Text ?? $"{_promptBuffer}{Environment.NewLine}";
                Render(clearScreen: true);
                return true;
            case PromptInputKind.HistoryPrevious:
                _promptBuffer = _promptHistory.Previous(_promptBuffer);
                Render(clearScreen: true);
                return true;
            case PromptInputKind.HistoryNext:
                _promptBuffer = _promptHistory.Next();
                Render(clearScreen: true);
                return true;
            case PromptInputKind.ScrollPageUp:
                ScrollActivePaneUp();
                Render(clearScreen: true);
                return true;
            case PromptInputKind.ScrollPageDown:
                ScrollActivePaneDown();
                Render(clearScreen: true);
                return true;
            case PromptInputKind.ScrollBottom:
                ScrollBottom();
                Render(clearScreen: true);
                return true;
            case PromptInputKind.FocusNext:
                CycleFocus(forward: true);
                Render(clearScreen: true);
                return true;
            case PromptInputKind.FocusPrevious:
                CycleFocus(forward: false);
                Render(clearScreen: true);
                return true;
            case PromptInputKind.ToggleHelp:
                ToggleHelpOverlay();
                Render(clearScreen: true);
                return true;
            case PromptInputKind.ToggleSession:
                await ToggleSessionOverlayAsync(options, cancellationToken);
                Render(clearScreen: true);
                return true;
            case PromptInputKind.TogglePty:
                await TogglePtyOverlayAsync(cancellationToken);
                Render(clearScreen: true);
                return true;
            default:
                return false;
        }
    }

    private async Task<bool> TryHandleSlashCommandAsync(string prompt, CancellationToken cancellationToken)
    {
        switch (prompt)
        {
            case "/help":
                ToggleHelpOverlay();
                return true;
            case "/session":
                await ToggleSessionOverlayAsync(new ReplLaunchOptions(_currentSession?.Id, _currentSession?.Model, _currentPermissionMode), cancellationToken);
                return true;
            case "/tasks":
                await ShowTasksOverlayAsync(cancellationToken);
                return true;
            case "/pty":
                await TogglePtyOverlayAsync(cancellationToken);
                return true;
            case "/clear":
                _transcriptViewport = new TerminalViewportState(PageSize: _transcriptViewport.PageSize);
                _contextViewport = new TerminalViewportState(PageSize: _contextViewport.PageSize);
                _activityState = TerminalActivityState.Cleared;
                _activityDetail = "Screen cleared. Session history is preserved.";
                return true;
            case "/bottom":
                ScrollBottom();
                _activityState = TerminalActivityState.Ready;
                _activityDetail = "Returned to live output.";
                return true;
            default:
                if (prompt.StartsWith("/tasks ", StringComparison.OrdinalIgnoreCase))
                {
                    var taskId = prompt["/tasks ".Length..].Trim();
                    if (!string.IsNullOrWhiteSpace(taskId))
                    {
                        await ShowTaskInspectionOverlayAsync(taskId, cancellationToken);
                        return true;
                    }
                }

                if (prompt.StartsWith("/pty ", StringComparison.OrdinalIgnoreCase))
                {
                    var args = prompt["/pty ".Length..].Trim();
                    if (args.StartsWith("close-exited", StringComparison.OrdinalIgnoreCase))
                    {
                        var pruned = await _ptyManager.PruneExitedAsync(cancellationToken);
                        _activityState = TerminalActivityState.Ready;
                        _activityDetail = pruned == 0 ? "No exited PTY sessions to remove." : $"Removed {pruned} exited PTY session(s).";
                        await RefreshPtyOverlayAsync();
                        return true;
                    }

                    if (args.StartsWith("close ", StringComparison.OrdinalIgnoreCase))
                    {
                        var closeSessionId = args["close ".Length..].Trim();
                        if (!string.IsNullOrWhiteSpace(closeSessionId))
                        {
                            var closed = await _ptyManager.CloseAsync(closeSessionId, cancellationToken);
                            _activityState = TerminalActivityState.Ready;
                            _activityDetail = closed is null
                                ? $"PTY session '{closeSessionId}' was not found."
                                : $"Closed PTY session {closed.SessionId}.";
                            await RefreshPtyOverlayAsync();
                            return true;
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(args))
                    {
                        await ShowPtyOverlayAsync(args, focusSession: true, cancellationToken);
                        return true;
                    }
                }

                return false;
        }
    }

    private void ApplyStreamEvent(QueryStreamEvent streamEvent)
    {
        switch (streamEvent)
        {
            case UserTurnAcceptedEvent accepted:
                _currentSession = accepted.Session;
                _draft = new StreamingAssistantDraft(string.Empty, true, null, "Waiting for model response...");
                _activityState = TerminalActivityState.WaitingForModel;
                _activityDetail = "Waiting for model response...";
                MarkTranscriptLiveUpdate();
                break;
            case AssistantTextDeltaStreamEvent delta:
                var currentText = _draft?.Text ?? string.Empty;
                _draft = new StreamingAssistantDraft($"{currentText}{delta.DeltaText}", true, null, "Streaming assistant response...");
                _activityState = TerminalActivityState.StreamingResponse;
                _activityDetail = "Streaming assistant response...";
                MarkTranscriptLiveUpdate();
                break;
            case AssistantMessageCommittedEvent committed:
                _currentSession = committed.Session;
                _draft = null;
                _activityState = TerminalActivityState.Ready;
                _activityDetail = null;
                MarkTranscriptLiveUpdate();
                break;
            case ToolCallRequestedEvent toolCallRequested:
                _draft = new StreamingAssistantDraft(string.Empty, true, toolCallRequested.ToolCall.Name, $"Preparing tool {toolCallRequested.ToolCall.Name}...");
                _activityState = TerminalActivityState.RunningTool;
                _activityDetail = $"Preparing tool {toolCallRequested.ToolCall.Name}...";
                MarkTranscriptLiveUpdate();
                break;
            case PermissionDecisionStreamEvent permissionEvent:
                _activityState = permissionEvent.Decision.Kind == PermissionDecisionKind.Ask
                    ? TerminalActivityState.AwaitingApproval
                    : TerminalActivityState.RunningTool;
                _activityDetail = permissionEvent.Decision.Reason;
                _draft = new StreamingAssistantDraft(string.Empty, true, permissionEvent.ToolCall.Name, permissionEvent.Decision.Reason);
                break;
            case EditPreviewGeneratedEvent previewEvent:
                _currentSession = previewEvent.Session;
                _overlay = new TuiOverlayState(
                    TuiOverlayKind.EditPreview,
                    $"Edit preview: {previewEvent.ToolCall.Name}",
                    previewEvent.Preview.Diff);
                _focus = TuiFocusTarget.Overlay;
                _activityState = TerminalActivityState.ReviewingEdits;
                _activityDetail = previewEvent.Preview.Summary;
                MarkTranscriptLiveUpdate();
                break;
            case EditApprovalRecordedEvent editApproval:
                _currentSession = editApproval.Session;
                _overlay = null;
                _focus = TuiFocusTarget.Composer;
                _activityState = editApproval.Approved ? TerminalActivityState.RunningTool : TerminalActivityState.ReviewingEdits;
                _activityDetail = editApproval.Summary;
                MarkTranscriptLiveUpdate();
                break;
            case ToolResultCommittedEvent committedTool:
                _currentSession = committedTool.Session;
                _activityState = TerminalActivityState.RunningTool;
                _activityDetail = committedTool.Result.Success
                    ? $"Tool {committedTool.ToolCall.Name} completed."
                    : $"Tool {committedTool.ToolCall.Name} failed.";
                MarkTranscriptLiveUpdate();
                break;
            case PluginHookRecordedEvent hookRecorded:
                _currentSession = hookRecorded.Session;
                _activityState = hookRecorded.Result.Success ? TerminalActivityState.RunningTool : TerminalActivityState.Error;
                _activityDetail = $"{hookRecorded.Result.Plugin.Name}:{hookRecorded.Result.Hook.Kind} -> {hookRecorded.Result.Message}";
                MarkTranscriptLiveUpdate();
                break;
            case TaskStartedStreamEvent taskStarted:
                _activityState = TerminalActivityState.RunningTool;
                _activityDetail = $"Task {taskStarted.Task.Id} started: {taskStarted.Task.Title}";
                break;
            case TaskUpdatedStreamEvent taskUpdated:
                _activityState = TerminalActivityState.RunningTool;
                _activityDetail = $"Task {taskUpdated.Task.Id}: {taskUpdated.Event.Message}";
                break;
            case TaskCompletedStreamEvent taskCompleted:
                _activityState = TerminalActivityState.RunningTool;
                _activityDetail = $"Task {taskCompleted.Task.Id} completed: {taskCompleted.Result.Summary}";
                break;
            case TaskFailedStreamEvent taskFailed:
                _activityState = TerminalActivityState.Error;
                _activityDetail = $"Task {taskFailed.Task.Id} failed: {taskFailed.Result.Error ?? taskFailed.Result.Summary}";
                break;
            case TaskCanceledStreamEvent taskCanceled:
                _activityState = TerminalActivityState.Interrupted;
                _activityDetail = $"Task {taskCanceled.Task.Id} canceled: {taskCanceled.Event.Message}";
                break;
            case TurnCompletedStreamEvent completed:
                _currentSession = completed.Result.Session;
                _draft = null;
                _activityState = TerminalActivityState.Ready;
                _activityDetail = null;
                break;
            case TurnFailedStreamEvent failed:
                _draft = null;
                _activityState = TerminalActivityState.Error;
                _activityDetail = failed.Message;
                _error = failed.Message;
                _overlay = new TuiOverlayState(TuiOverlayKind.Error, "Turn failed", failed.Message);
                _focus = TuiFocusTarget.Overlay;
                break;
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

        return await _conversationStore.CreateAsync("TUI session", options.Model ?? "claude-sonnet-4-5", cancellationToken);
    }

    private void Render(bool clearScreen)
    {
        if (_currentSession is null)
        {
            return;
        }

        var size = _terminalSession.GetTerminalSize();
        var layout = new TuiLayoutState(size.Width, size.Height, Math.Max(50, size.Width - 32), Math.Min(30, Math.Max(24, size.Width / 4)));
        var state = new TuiState(
            _currentSession,
            _currentPermissionMode,
            GetTranscriptMessages(_currentSession),
            GetVisibleTasks(),
            _promptBuffer,
            _focus,
            _overlay,
            layout,
            _transcriptViewport,
            _contextViewport,
            _draft,
            _ptyManager.State,
            _activityState,
            _activityDetail,
            _error,
            clearScreen,
            true);
        _terminalSession.RenderFrame(_tuiRenderer.Render(state));
    }

    private IReadOnlyList<ConversationMessage> GetTranscriptMessages(ConversationSession session)
    {
        var messages = session.Messages;
        if (messages.Count <= _transcriptViewport.PageSize)
        {
            return messages;
        }

        var offset = Math.Clamp(_transcriptViewport.ScrollOffsetFromBottom, 0, Math.Max(0, messages.Count - _transcriptViewport.PageSize));
        var endExclusive = Math.Max(0, messages.Count - offset);
        var start = Math.Max(0, endExclusive - _transcriptViewport.PageSize);
        return messages.Skip(start).Take(endExclusive - start).ToArray();
    }

    private IReadOnlyList<TaskRecord> GetVisibleTasks()
    {
        try
        {
            return _taskManager.ListAsync(CancellationToken.None).GetAwaiter().GetResult().Take(8).ToArray();
        }
        catch
        {
            return [];
        }
    }

    private void ScrollActivePaneUp()
    {
        switch (_focus)
        {
            case TuiFocusTarget.Context:
                _contextViewport = _contextViewport with
                {
                    ScrollOffsetFromBottom = _contextViewport.ScrollOffsetFromBottom + _contextViewport.PageSize,
                    FollowLiveOutput = false,
                    HasBufferedLiveOutput = true
                };
                break;
            default:
                _transcriptViewport = _transcriptViewport with
                {
                    ScrollOffsetFromBottom = _transcriptViewport.ScrollOffsetFromBottom + _transcriptViewport.PageSize,
                    FollowLiveOutput = false,
                    HasBufferedLiveOutput = true
                };
                break;
        }
    }

    private void ScrollActivePaneDown()
    {
        switch (_focus)
        {
            case TuiFocusTarget.Context:
                var newContextOffset = Math.Max(0, _contextViewport.ScrollOffsetFromBottom - _contextViewport.PageSize);
                _contextViewport = _contextViewport with
                {
                    ScrollOffsetFromBottom = newContextOffset,
                    FollowLiveOutput = newContextOffset == 0,
                    HasBufferedLiveOutput = newContextOffset == 0 ? false : _contextViewport.HasBufferedLiveOutput
                };
                break;
            default:
                var newOffset = Math.Max(0, _transcriptViewport.ScrollOffsetFromBottom - _transcriptViewport.PageSize);
                _transcriptViewport = _transcriptViewport with
                {
                    ScrollOffsetFromBottom = newOffset,
                    FollowLiveOutput = newOffset == 0,
                    HasBufferedLiveOutput = newOffset == 0 ? false : _transcriptViewport.HasBufferedLiveOutput
                };
                break;
        }
    }

    private void ScrollBottom()
    {
        if (_focus == TuiFocusTarget.Context)
        {
            _contextViewport = _contextViewport with { ScrollOffsetFromBottom = 0, FollowLiveOutput = true, HasBufferedLiveOutput = false };
            return;
        }

        _transcriptViewport = _transcriptViewport with { ScrollOffsetFromBottom = 0, FollowLiveOutput = true, HasBufferedLiveOutput = false };
    }

    private void CycleFocus(bool forward)
    {
        var order = new[] { TuiFocusTarget.Transcript, TuiFocusTarget.Composer, TuiFocusTarget.Context };
        if (_overlay is not null)
        {
            _focus = TuiFocusTarget.Overlay;
            return;
        }

        var index = Array.IndexOf(order, _focus);
        if (index < 0)
        {
            _focus = TuiFocusTarget.Composer;
            return;
        }

        var next = forward
            ? (index + 1) % order.Length
            : (index + order.Length - 1) % order.Length;
        _focus = order[next];
    }

    private void ToggleHelpOverlay()
    {
        if (_overlay?.Kind == TuiOverlayKind.Help)
        {
            _overlay = null;
            _focus = TuiFocusTarget.Composer;
            return;
        }

        _overlay = new TuiOverlayState(
            TuiOverlayKind.Help,
            "Keyboard shortcuts",
            "Tab/Shift+Tab: cycle focus" + Environment.NewLine +
            "PgUp/PgDn: scroll focused pane" + Environment.NewLine +
            "End: jump focused pane to bottom" + Environment.NewLine +
            "F1: help" + Environment.NewLine +
            "F2: session details" + Environment.NewLine +
            "F3: PTY sessions" + Environment.NewLine +
            "Ctrl+C: interrupt PTY or active turn" + Environment.NewLine +
            "Slash commands: /help /session /tasks [/tasks <id>] /pty [/pty <id>] [/pty close <id>] [/pty close-exited] /clear /bottom /exit");
        _focus = TuiFocusTarget.Overlay;
    }

    private async Task ToggleSessionOverlayAsync(ReplLaunchOptions options, CancellationToken cancellationToken)
    {
        if (_overlay?.Kind == TuiOverlayKind.Session)
        {
            _overlay = null;
            _focus = TuiFocusTarget.Composer;
            return;
        }

        var tasks = await _taskManager.ListAsync(cancellationToken);
        _overlay = new TuiOverlayState(
            TuiOverlayKind.Session,
            "Session details",
            $"Session: {_currentSession?.Id ?? options.SessionId}" + Environment.NewLine +
            $"Model: {_currentSession?.Model ?? options.Model}" + Environment.NewLine +
            $"Permission: {_currentPermissionMode}" + Environment.NewLine +
            $"Messages: {_currentSession?.Messages.Count ?? 0}" + Environment.NewLine +
            $"Recent tasks: {tasks.Count}");
        _focus = TuiFocusTarget.Overlay;
    }

    private async Task ShowTasksOverlayAsync(CancellationToken cancellationToken)
    {
        var tasks = await _taskManager.ListAsync(cancellationToken);
        var content = tasks.Count == 0
            ? "No tasks found."
            : string.Join(
                Environment.NewLine,
                tasks.Take(8).Select(task =>
                    $"{task.Id} | {task.Status} | {task.Title}{Environment.NewLine}  updated={task.UpdatedAtUtc:O}{Environment.NewLine}  summary={task.Result?.Summary ?? task.LastStatusMessage ?? "(none)"}"));
        if (tasks.Count > 0)
        {
            content = $"{content}{Environment.NewLine}{Environment.NewLine}Use /tasks <id> to inspect a worker transcript.";
        }

        _overlay = new TuiOverlayState(TuiOverlayKind.Session, "Recent tasks", content);
        _focus = TuiFocusTarget.Overlay;
    }

    private async Task ShowTaskInspectionOverlayAsync(string taskId, CancellationToken cancellationToken)
    {
        var inspection = await _taskManager.InspectAsync(taskId, cancellationToken);
        if (inspection is null)
        {
            _overlay = new TuiOverlayState(TuiOverlayKind.Error, "Task not found", $"Task '{taskId}' was not found.");
            _focus = TuiFocusTarget.Overlay;
            return;
        }

        var recentEvents = inspection.RecentEvents.Count == 0
            ? "(none)"
            : string.Join(Environment.NewLine, inspection.RecentEvents.Select(taskEvent => $"{taskEvent.TimestampUtc:HH:mm:ss} | {taskEvent.Status} | {taskEvent.Message}"));
        var content = string.Join(
            Environment.NewLine,
            [
                $"Task: {inspection.Task.Id}",
                $"Status: {inspection.Task.Status}",
                $"Title: {inspection.Task.Title}",
                $"WorkerSession: {inspection.Worker.WorkerSessionId}",
                $"WorkerMessages: {inspection.Worker.MessageCount}",
                $"WorkerUpdatedAtUtc: {inspection.Worker.UpdatedAtUtc:O}",
                $"Summary: {inspection.Task.Result?.Summary ?? inspection.Task.LastStatusMessage ?? "(none)"}",
                "RecentEvents:",
                recentEvents,
                "WorkerTranscriptTail:",
                string.IsNullOrWhiteSpace(inspection.Worker.TranscriptTail) ? "(none)" : inspection.Worker.TranscriptTail
            ]);

        _overlay = new TuiOverlayState(TuiOverlayKind.Session, $"Task details: {inspection.Task.Id}", content);
        _focus = TuiFocusTarget.Overlay;
    }

    private async Task TogglePtyOverlayAsync(CancellationToken cancellationToken)
    {
        if (_overlay?.Title == "PTY sessions" || (_overlay?.Title?.StartsWith("PTY details:", StringComparison.Ordinal) ?? false))
        {
            _overlay = null;
            _focus = TuiFocusTarget.Composer;
            return;
        }

        await ShowPtyOverlayAsync(_ptyManager.State.CurrentSessionId, focusSession: false, cancellationToken);
    }

    private async Task ShowPtyOverlayAsync(string? sessionId, bool focusSession, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (focusSession && !string.IsNullOrWhiteSpace(sessionId))
        {
            try
            {
                await _ptyManager.FocusAsync(sessionId, cancellationToken);
            }
            catch (Exception ex)
            {
                _overlay = new TuiOverlayState(TuiOverlayKind.Error, "PTY session not found", ex.Message);
                _focus = TuiFocusTarget.Overlay;
                return;
            }
        }

        var state = _ptyManager.State;
        if (state.Sessions.Count == 0)
        {
            _overlay = new TuiOverlayState(TuiOverlayKind.Session, "PTY sessions", "No PTY sessions.");
            _focus = TuiFocusTarget.Overlay;
            return;
        }

        var selected = !string.IsNullOrWhiteSpace(sessionId)
            ? state.Sessions.FirstOrDefault(session => string.Equals(session.SessionId, sessionId, StringComparison.Ordinal))
            : state.Sessions.FirstOrDefault(session => session.IsCurrent) ?? state.Sessions[0];
        var detail = selected is null
            ? "No PTY session selected."
            : await BuildPtyDetailAsync(selected.SessionId, cancellationToken);
        var list = string.Join(
            Environment.NewLine,
            state.Sessions.Take(8).Select(session =>
                $"{(session.IsCurrent ? "*" : "-")} {session.SessionId} | {(session.IsRunning ? "running" : "stopped")} | {session.Command}"));
        var content = $"Sessions:{Environment.NewLine}{list}{Environment.NewLine}{Environment.NewLine}{detail}{Environment.NewLine}{Environment.NewLine}Commands: /pty <id> focus | /pty close <id> | /pty close-exited";

        _overlay = new TuiOverlayState(
            TuiOverlayKind.Session,
            selected is null ? "PTY sessions" : $"PTY details: {selected.SessionId}",
            content);
        _focus = TuiFocusTarget.Overlay;
    }

    private async Task RefreshPtyOverlayAsync()
    {
        var selectedSessionId = _overlay?.Title?.StartsWith("PTY details:", StringComparison.Ordinal) == true
            ? _overlay.Title["PTY details: ".Length..].Trim()
            : _ptyManager.State.CurrentSessionId;
        await ShowPtyOverlayAsync(selectedSessionId, focusSession: false, CancellationToken.None);
    }

    private async Task<string> BuildPtyDetailAsync(string sessionId, CancellationToken cancellationToken)
    {
        var state = await _ptyManager.ReadAsync(sessionId, cancellationToken);
        if (state is null)
        {
            return "Selected PTY session was not found.";
        }

        return string.Join(
            Environment.NewLine,
            [
                "Selected:",
                $"session={state.SessionId}",
                $"command={state.Command}",
                $"cwd={state.WorkingDirectory}",
                $"running={state.IsRunning}",
                $"exitCode={(state.ExitCode.HasValue ? state.ExitCode.Value.ToString() : "n/a")}",
                $"outputClipped={state.IsOutputClipped}",
                string.Empty,
                string.IsNullOrWhiteSpace(state.RecentOutput) ? "(no output yet)" : state.RecentOutput.TrimEnd()
            ]);
    }

    private void HandlePtyStateChanged(PtyManagerState state)
    {
        if (_currentSession is null)
        {
            return;
        }

        if (state.CurrentSession is not null && state.CurrentSession.IsRunning)
        {
            _activityState = TerminalActivityState.RunningTool;
            _activityDetail = $"PTY session active: {state.CurrentSession.Command}";
        }

        if (_overlay?.Title == "PTY sessions" || (_overlay?.Title?.StartsWith("PTY details:", StringComparison.Ordinal) ?? false))
        {
            _ = RefreshPtyOverlayAsync();
        }

        MarkContextLiveUpdate();
        Render(clearScreen: true);
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

            _activityState = TerminalActivityState.RunningTool;
            _activityDetail = $"Task {task.Id} | {task.Status} | {taskEvent.Message}";
            if (_overlay?.Title == "Recent tasks")
            {
                await ShowTasksOverlayAsync(CancellationToken.None);
            }
            else if (_overlay?.Title == $"Task details: {task.Id}")
            {
                await ShowTaskInspectionOverlayAsync(task.Id, CancellationToken.None);
            }
            MarkContextLiveUpdate();
            Render(clearScreen: true);
        }
        catch
        {
            // ignore background UI update failures
        }
    }

    private void MarkTranscriptLiveUpdate()
    {
        if (_transcriptViewport.FollowLiveOutput)
        {
            _transcriptViewport = _transcriptViewport with { HasBufferedLiveOutput = false };
            return;
        }

        _transcriptViewport = _transcriptViewport with { HasBufferedLiveOutput = true };
    }

    private void MarkContextLiveUpdate()
    {
        if (_contextViewport.FollowLiveOutput)
        {
            _contextViewport = _contextViewport with { HasBufferedLiveOutput = false };
            return;
        }

        _contextViewport = _contextViewport with { HasBufferedLiveOutput = true };
    }

    private sealed class TuiApprovalHandler : IToolApprovalHandler
    {
        private readonly TuiHost _owner;

        public TuiApprovalHandler(TuiHost owner)
        {
            _owner = owner;
        }

        public async Task<bool> ApproveAsync(
            ITool tool,
            ToolCall toolCall,
            PermissionDecision permissionDecision,
            CancellationToken cancellationToken)
        {
            _owner._overlay = new TuiOverlayState(
                TuiOverlayKind.Approval,
                $"Approve {toolCall.Name}",
                permissionDecision.Reason,
                true);
            _owner._focus = TuiFocusTarget.Overlay;
            _owner._activityState = TerminalActivityState.AwaitingApproval;
            _owner._activityDetail = permissionDecision.Reason;
            _owner.Render(clearScreen: true);
            var approved = await _owner._terminalSession.ConfirmAsync($"Approve {toolCall.Name}?", cancellationToken);
            _owner._overlay = null;
            _owner._focus = TuiFocusTarget.Composer;
            return approved;
        }
    }
}
