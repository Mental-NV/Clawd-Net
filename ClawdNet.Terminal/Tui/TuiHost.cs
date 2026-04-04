using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Exceptions;
using ClawdNet.Core.Models;
using ClawdNet.Terminal.Abstractions;
using ClawdNet.Terminal.Defaults;
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
    private readonly IProviderCatalog _providerCatalog;
    private readonly IPlatformLauncher _platformLauncher;
    private readonly IToolRegistry _toolRegistry;
    private readonly PromptHistoryBuffer _promptHistory = new();

    private ConversationSession? _currentSession;
    private PermissionMode _currentPermissionMode = PermissionMode.Default;
    private TerminalViewportState _transcriptViewport = new(PageSize: 18);
    private TerminalViewportState _contextViewport = new(PageSize: 10);
    private StreamingAssistantDraft? _draft;
    private string _promptBuffer = string.Empty;
    private TuiFocusTarget _focus = TuiFocusTarget.Composer;
    private TuiDrawerState? _drawer;
    private TuiOverlayState? _overlay;
    private TerminalActivityState _activityState = TerminalActivityState.Ready;
    private string? _activityDetail;
    private string? _error;
    private CancellationTokenSource? _activeTurnCancellation;
    private readonly List<string> _activityFeed = [];
    private string? _selectedSessionId;
    private string? _selectedTaskId;
    private string? _selectedPtySessionId;
    private string? _ptyFullScreenSessionId;
    private readonly object _ptyOutputLock = new();
    private string _ptyFullScreenOutput = string.Empty;
    private int _ptyFullScreenScrollOffset;
    private readonly List<string> _ptyFullScreenOutputHistory = [];

    public TuiHost(
        ITerminalSession terminalSession,
        IConversationStore conversationStore,
        IQueryEngine queryEngine,
        ITuiRenderer tuiRenderer,
        IPtyManager ptyManager,
        ITaskManager taskManager,
        IProviderCatalog? providerCatalog = null,
        IPlatformLauncher? platformLauncher = null,
        IToolRegistry? toolRegistry = null)
    {
        _terminalSession = terminalSession;
        _conversationStore = conversationStore;
        _queryEngine = queryEngine;
        _tuiRenderer = tuiRenderer;
        _ptyManager = ptyManager;
        _taskManager = taskManager;
        _providerCatalog = providerCatalog ?? new TerminalFallbackProviderCatalog();
        _platformLauncher = platformLauncher ?? new TerminalNullPlatformLauncher();
        _toolRegistry = toolRegistry ?? new TerminalFallbackToolRegistry();
    }

    public async Task<CommandExecutionResult> RunAsync(ReplLaunchOptions options, CancellationToken cancellationToken)
    {
        ConversationSession session;
        try
        {
            session = await LoadOrCreateSessionAsync(options, cancellationToken);
        }
        catch (ModelProviderConfigurationException ex)
        {
            return CommandExecutionResult.Failure(ex.Message, 2);
        }
        catch (ConversationStoreException ex)
        {
            return CommandExecutionResult.Failure(ex.Message, 3);
        }

        _currentSession = session;
        _currentPermissionMode = options.PermissionMode;
        _promptBuffer = string.IsNullOrWhiteSpace(options.InitialPrompt) ? string.Empty : options.InitialPrompt.Trim();
        _focus = TuiFocusTarget.Composer;
        _drawer = null;
        _overlay = null;
        _draft = null;
        _activityState = TerminalActivityState.Ready;
        _activityDetail = null;
        _error = null;
        _promptHistory.ResetNavigation();
        _transcriptViewport = new TerminalViewportState(PageSize: 18);
        _contextViewport = new TerminalViewportState(PageSize: 10);
        _activityFeed.Clear();
        AddActivityFeed("TUI session ready.");

        // If no explicit session was requested and prior sessions exist, show resume picker
        if (string.IsNullOrWhiteSpace(options.SessionId))
        {
            var allSessions = await _conversationStore.ListAsync(cancellationToken);
            if (allSessions.Count > 1)
            {
                AddActivityFeed($"session | {allSessions.Count} sessions found, showing resume picker");
                await ToggleSessionDrawerAsync(options, cancellationToken);
            }
        }

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

                // If full-screen PTY mode is active, handle PTY-specific input
                if (_ptyFullScreenSessionId is not null)
                {
                    await HandlePtyFullScreenInputAsync(cancellationToken);
                    continue;
                }

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
                                       new QueryRequest(prompt, session.Id, session.Model, 8, options.PermissionMode, new TuiApprovalHandler(this), true, session.Provider),
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
                catch (ModelProviderConfigurationException ex)
                {
                    _activeTurnCancellation = null;
                    _draft = null;
                    _activityState = TerminalActivityState.Error;
                    _activityDetail = ex.Message;
                    _error = ex.Message;
                    _overlay = new TuiOverlayState(
                        TuiOverlayKind.Error,
                        "Configuration error",
                        ex.Message,
                        [new TuiOverlaySection("Error", [ex.Message])]);
                    AddActivityFeed($"error | {ex.Message}");
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
                    _overlay = new TuiOverlayState(
                        TuiOverlayKind.Error,
                        "Conversation error",
                        ex.Message,
                        [new TuiOverlaySection("Error", [ex.Message])]);
                    AddActivityFeed($"error | {ex.Message}");
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
                await ToggleSessionDrawerAsync(options, cancellationToken);
                Render(clearScreen: true);
                return true;
            case PromptInputKind.TogglePty:
                await TogglePtyDrawerAsync(cancellationToken);
                Render(clearScreen: true);
                return true;
            case PromptInputKind.ToggleTasks:
                await ToggleTasksDrawerAsync(cancellationToken);
                Render(clearScreen: true);
                return true;
            case PromptInputKind.ToggleActivity:
                ToggleActivityDrawer();
                Render(clearScreen: true);
                return true;
            case PromptInputKind.DrawerPreviousItem:
                await MoveDrawerSelectionAsync(-1, cancellationToken);
                Render(clearScreen: true);
                return true;
            case PromptInputKind.DrawerNextItem:
                await MoveDrawerSelectionAsync(1, cancellationToken);
                Render(clearScreen: true);
                return true;
            case PromptInputKind.DrawerOpenSelected:
                await OpenSelectedDrawerItemAsync(options, cancellationToken);
                Render(clearScreen: true);
                return true;
            case PromptInputKind.DismissSurface:
                DismissSurface();
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
                await ToggleSessionDrawerAsync(new ReplLaunchOptions(_currentSession?.Id, _currentSession?.Model, _currentPermissionMode, _currentSession?.Provider), cancellationToken);
                return true;
            case "/provider":
                if (_currentSession is not null)
                {
                    _activityState = TerminalActivityState.ShowingSession;
                    _activityDetail = $"Provider {_currentSession.Provider} | model={_currentSession.Model}";
                }
                return true;
            case "/tasks":
                await ToggleTasksDrawerAsync(cancellationToken);
                return true;
            case "/pty":
                await TogglePtyDrawerAsync(cancellationToken);
                return true;
            case "/activity":
                ToggleActivityDrawer();
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
            case "/status":
                await ShowSessionStatusAsync(cancellationToken);
                return true;
            case "/context":
                await ShowSessionContextAsync(cancellationToken);
                return true;
            case "/permissions":
                ShowPermissionsOverlay();
                return true;
            case "/config":
                ShowConfigOverlay();
                return true;
            default:
                if (prompt.StartsWith("/tasks ", StringComparison.OrdinalIgnoreCase))
                {
                    var taskId = prompt["/tasks ".Length..].Trim();
                    if (!string.IsNullOrWhiteSpace(taskId))
                    {
                        await ShowTaskDrawerAsync(taskId, cancellationToken);
                        return true;
                    }
                }

                if (prompt.StartsWith("/provider ", StringComparison.OrdinalIgnoreCase))
                {
                    var args = prompt["/provider ".Length..].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (args.Length > 0 && _currentSession is not null)
                    {
                        try
                        {
                            _currentSession = await UpdateSessionProviderAsync(_currentSession, args[0], args.Length > 1 ? args[1] : null, cancellationToken);
                            _activityState = TerminalActivityState.ShowingSession;
                            _activityDetail = $"Provider updated to {_currentSession.Provider} | model={_currentSession.Model}";
                            AddActivityFeed($"provider | {_currentSession.Provider} | {_currentSession.Model}");
                        }
                        catch (ModelProviderConfigurationException ex)
                        {
                            _activityState = TerminalActivityState.Error;
                            _activityDetail = ex.Message;
                            _overlay = new TuiOverlayState(TuiOverlayKind.Error, "Provider error", ex.Message, [new TuiOverlaySection("Error", [ex.Message])]);
                            _focus = TuiFocusTarget.Overlay;
                        }

                        return true;
                    }
                }

                if (prompt.StartsWith("/open ", StringComparison.OrdinalIgnoreCase))
                {
                    var result = await OpenFromSlashCommandAsync(prompt["/open ".Length..], cancellationToken);
                    _activityState = result.Success ? TerminalActivityState.ShowingSession : TerminalActivityState.Error;
                    _activityDetail = result.Success ? result.Message : result.Error;
                    AddActivityFeed(result.Success ? $"platform | {result.Message}" : $"platform | error | {result.Error}");
                    return true;
                }

                if (prompt.StartsWith("/browse ", StringComparison.OrdinalIgnoreCase))
                {
                    var url = prompt["/browse ".Length..].Trim();
                    var result = await _platformLauncher.OpenUrlAsync(url, cancellationToken);
                    _activityState = result.Success ? TerminalActivityState.ShowingSession : TerminalActivityState.Error;
                    _activityDetail = result.Success ? result.Message : result.Error;
                    AddActivityFeed(result.Success ? $"platform | {result.Message}" : $"platform | error | {result.Error}");
                    return true;
                }

                if (prompt.StartsWith("/pty ", StringComparison.OrdinalIgnoreCase))
                {
                    var args = prompt["/pty ".Length..].Trim();
                    if (args.StartsWith("close-exited", StringComparison.OrdinalIgnoreCase))
                    {
                        var pruned = await _ptyManager.PruneExitedAsync(cancellationToken);
                        _activityState = TerminalActivityState.Ready;
                        _activityDetail = pruned == 0 ? "No exited PTY sessions to remove." : $"Removed {pruned} exited PTY session(s).";
                        AddActivityFeed(_activityDetail);
                        await RefreshPtyDrawerAsync();
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
                            AddActivityFeed(_activityDetail);
                            await RefreshPtyDrawerAsync();
                            return true;
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(args))
                    {
                        await ShowPtyDrawerAsync(args, focusSession: true, cancellationToken);
                        return true;
                    }
                }

                if (prompt.StartsWith("/pty status ", StringComparison.OrdinalIgnoreCase))
                {
                    var ptySessionId = prompt["/pty status ".Length..].Trim();
                    await ShowPtyStatusAsync(ptySessionId, cancellationToken);
                    return true;
                }

                if (prompt.StartsWith("/pty attach ", StringComparison.OrdinalIgnoreCase))
                {
                    var ptySessionId = prompt["/pty attach ".Length..].Trim();
                    if (!string.IsNullOrWhiteSpace(ptySessionId))
                    {
                        try
                        {
                            var focused = await _ptyManager.FocusAsync(ptySessionId, cancellationToken);
                            _activityState = TerminalActivityState.Ready;
                            _activityDetail = $"Attached to PTY session {ptySessionId}: {focused.Command}";
                            AddActivityFeed($"pty | attach | {ptySessionId}");
                            await RefreshPtyDrawerAsync();
                            return true;
                        }
                        catch (InvalidOperationException ex)
                        {
                            _activityState = TerminalActivityState.Error;
                            _activityDetail = ex.Message;
                            return true;
                        }
                    }
                }

                if (prompt.StartsWith("/pty detach", StringComparison.OrdinalIgnoreCase))
                {
                    var current = _ptyManager.State.CurrentSessionId;
                    if (string.IsNullOrWhiteSpace(current))
                    {
                        _activityState = TerminalActivityState.Error;
                        _activityDetail = "No active PTY session to detach.";
                    }
                    else
                    {
                        // Detach by focusing another running session or clearing focus
                        var sessions = await _ptyManager.ListAsync(cancellationToken);
                        var otherRunning = sessions.FirstOrDefault(s => s.IsRunning && !string.Equals(s.SessionId, current, StringComparison.Ordinal));
                        if (otherRunning is not null)
                        {
                            await _ptyManager.FocusAsync(otherRunning.SessionId, cancellationToken);
                            _activityDetail = $"Detached from {current}, attached to {otherRunning.SessionId}.";
                        }
                        else
                        {
                            _activityDetail = $"Detached from {current}. No other running PTY sessions.";
                        }
                        _activityState = TerminalActivityState.Ready;
                        AddActivityFeed($"pty | detach | from {current}");
                        await RefreshPtyDrawerAsync();
                    }
                    return true;
                }

                if (prompt.StartsWith("/pty close-all", StringComparison.OrdinalIgnoreCase))
                {
                    var sessions = await _ptyManager.ListAsync(cancellationToken);
                    var closedCount = 0;
                    foreach (var session in sessions)
                    {
                        if (session.IsRunning)
                        {
                            await _ptyManager.CloseAsync(session.SessionId, cancellationToken);
                            closedCount++;
                        }
                    }
                    _activityState = TerminalActivityState.Ready;
                    _activityDetail = closedCount == 0 ? "No running PTY sessions to close." : $"Closed {closedCount} PTY session(s).";
                    AddActivityFeed(_activityDetail);
                    await RefreshPtyDrawerAsync();
                    return true;
                }

                if (prompt.StartsWith("/pty fullscreen", StringComparison.OrdinalIgnoreCase))
                {
                    var sessionId = prompt["/pty fullscreen".Length..].Trim();
                    if (string.IsNullOrWhiteSpace(sessionId))
                    {
                        // Use current session if not specified
                        sessionId = _ptyManager.State.CurrentSessionId;
                    }
                    if (!string.IsNullOrWhiteSpace(sessionId))
                    {
                        await EnterPtyFullScreenAsync(sessionId, cancellationToken);
                        return true;
                    }
                }

                if (prompt.StartsWith("/rename ", StringComparison.OrdinalIgnoreCase))
                {
                    var newName = prompt["/rename ".Length..].Trim();
                    if (!string.IsNullOrWhiteSpace(newName) && _currentSession is not null)
                    {
                        var updated = _currentSession with { Title = newName, UpdatedAtUtc = DateTimeOffset.UtcNow };
                        _currentSession = updated;
                        await _conversationStore.SaveAsync(updated, cancellationToken);
                        _activityState = TerminalActivityState.Ready;
                        _activityDetail = $"Session renamed to '{newName}'.";
                        AddActivityFeed($"session | rename | '{newName}'");
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
                AddActivityFeed("query | user turn accepted");
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
                AddActivityFeed($"tool | requested {toolCallRequested.ToolCall.Name}");
                MarkTranscriptLiveUpdate();
                break;
            case PermissionDecisionStreamEvent permissionEvent:
                _activityState = permissionEvent.Decision.Kind == PermissionDecisionKind.Ask
                    ? TerminalActivityState.AwaitingApproval
                    : TerminalActivityState.RunningTool;
                _activityDetail = permissionEvent.Decision.Reason;
                _draft = new StreamingAssistantDraft(string.Empty, true, permissionEvent.ToolCall.Name, permissionEvent.Decision.Reason);
                AddActivityFeed($"permission | {permissionEvent.ToolCall.Name} | {permissionEvent.Decision.Reason}");
                break;
            case EditPreviewGeneratedEvent previewEvent:
                _currentSession = previewEvent.Session;
                _overlay = new TuiOverlayState(
                    TuiOverlayKind.EditReview,
                    $"Edit preview: {previewEvent.ToolCall.Name}",
                    previewEvent.Preview.Summary,
                    [
                        new TuiOverlaySection(
                            "Summary",
                            [
                                $"files={previewEvent.Preview.FileCount}",
                                previewEvent.Preview.Summary
                            ]),
                        new TuiOverlaySection(
                            "Diff",
                            previewEvent.Preview.Diff.Split(Environment.NewLine).ToArray())
                    ]);
                _focus = TuiFocusTarget.Overlay;
                _activityState = TerminalActivityState.ReviewingEdits;
                _activityDetail = previewEvent.Preview.Summary;
                AddActivityFeed($"edit | preview | {previewEvent.Preview.Summary}");
                MarkTranscriptLiveUpdate();
                break;
            case EditApprovalRecordedEvent editApproval:
                _currentSession = editApproval.Session;
                _overlay = null;
                _focus = TuiFocusTarget.Composer;
                _activityState = editApproval.Approved ? TerminalActivityState.RunningTool : TerminalActivityState.ReviewingEdits;
                _activityDetail = editApproval.Summary;
                AddActivityFeed($"edit | {(editApproval.Approved ? "approved" : "rejected")} | {editApproval.Summary}");
                MarkTranscriptLiveUpdate();
                break;
            case ToolResultCommittedEvent committedTool:
                _currentSession = committedTool.Session;
                _activityState = TerminalActivityState.RunningTool;
                _activityDetail = committedTool.Result.Success
                    ? $"Tool {committedTool.ToolCall.Name} completed."
                    : $"Tool {committedTool.ToolCall.Name} failed.";
                AddActivityFeed($"tool | {committedTool.ToolCall.Name} | {(committedTool.Result.Success ? "completed" : "failed")}");
                MarkTranscriptLiveUpdate();
                break;
            case PluginHookRecordedEvent hookRecorded:
                _currentSession = hookRecorded.Session;
                _activityState = hookRecorded.Result.Success ? TerminalActivityState.RunningTool : TerminalActivityState.Error;
                _activityDetail = $"{hookRecorded.Result.Plugin.Name}:{hookRecorded.Result.Hook.Kind} -> {hookRecorded.Result.Message}";
                AddActivityFeed($"hook | {hookRecorded.Result.Plugin.Name}:{hookRecorded.Result.Hook.Kind} | {hookRecorded.Result.Message}");
                MarkTranscriptLiveUpdate();
                break;
            case TaskStartedStreamEvent taskStarted:
                _activityState = TerminalActivityState.RunningTool;
                _activityDetail = $"Task {taskStarted.Task.Id} started: {taskStarted.Task.Title}";
                AddActivityFeed($"task | started | {taskStarted.Task.Id}");
                break;
            case TaskUpdatedStreamEvent taskUpdated:
                _activityState = TerminalActivityState.RunningTool;
                _activityDetail = $"Task {taskUpdated.Task.Id}: {taskUpdated.Event.Message}";
                AddActivityFeed($"task | updated | {taskUpdated.Task.Id} | {taskUpdated.Event.Message}");
                break;
            case TaskCompletedStreamEvent taskCompleted:
                _activityState = TerminalActivityState.RunningTool;
                _activityDetail = $"Task {taskCompleted.Task.Id} completed: {taskCompleted.Result.Summary}";
                AddActivityFeed($"task | completed | {taskCompleted.Task.Id}");
                break;
            case TaskFailedStreamEvent taskFailed:
                _activityState = TerminalActivityState.Error;
                _activityDetail = $"Task {taskFailed.Task.Id} failed: {taskFailed.Result.Error ?? taskFailed.Result.Summary}";
                AddActivityFeed($"task | failed | {taskFailed.Task.Id}");
                break;
            case TaskCanceledStreamEvent taskCanceled:
                _activityState = TerminalActivityState.Interrupted;
                _activityDetail = $"Task {taskCanceled.Task.Id} canceled: {taskCanceled.Event.Message}";
                AddActivityFeed($"task | canceled | {taskCanceled.Task.Id}");
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
                _overlay = new TuiOverlayState(
                    TuiOverlayKind.Error,
                    "Turn failed",
                    failed.Message,
                    [new TuiOverlaySection("Error", [failed.Message])]);
                _focus = TuiFocusTarget.Overlay;
                AddActivityFeed($"error | turn failed | {failed.Message}");
                break;
        }
    }

    private async Task<ConversationSession> LoadOrCreateSessionAsync(ReplLaunchOptions options, CancellationToken cancellationToken)
    {
        // Handle --resume [value] first
        if (options.ResumeQuery is not null)
        {
            if (string.IsNullOrWhiteSpace(options.ResumeQuery))
            {
                // --resume without value: resume the most recent session
                var mostRecent = await _conversationStore.GetMostRecentAsync(cancellationToken);
                if (mostRecent is null)
                {
                    throw new ConversationStoreException("No sessions found to resume. Create a session first with 'session new' or start a new conversation.");
                }

                var provider = await _providerCatalog.ResolveAsync(
                    string.IsNullOrWhiteSpace(options.Provider) ? mostRecent.Provider : options.Provider,
                    cancellationToken);
                var resolvedModel = ResolveModel(mostRecent, options.Model, provider.Name, provider.DefaultModel);
                var updated = mostRecent with
                {
                    Provider = provider.Name,
                    Model = resolvedModel
                };
                if (!string.Equals(mostRecent.Provider, updated.Provider, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(mostRecent.Model, updated.Model, StringComparison.Ordinal))
                {
                    await _conversationStore.SaveAsync(updated, cancellationToken);
                }

                return updated;
            }

            // --resume with value: search for matching sessions
            var matches = await _conversationStore.SearchAsync(options.ResumeQuery, cancellationToken);
            if (matches.Count == 0)
            {
                throw new ConversationStoreException($"No sessions found matching '{options.ResumeQuery}'.");
            }

            if (matches.Count > 1)
            {
                var matchList = string.Join(Environment.NewLine, matches.Take(5).Select(m => $"  {m.Id} | {m.Title} | {m.UpdatedAtUtc:O}"));
                throw new ConversationStoreException($"Multiple sessions match '{options.ResumeQuery}':{Environment.NewLine}{matchList}{Environment.NewLine}Use a more specific query or --continue for the most recent.");
            }

            var matchedSession = matches[0];
            var matchedProvider = await _providerCatalog.ResolveAsync(
                string.IsNullOrWhiteSpace(options.Provider) ? matchedSession.Provider : options.Provider,
                cancellationToken);
            var matchedModel = ResolveModel(matchedSession, options.Model, matchedProvider.Name, matchedProvider.DefaultModel);
            var matchedUpdated = matchedSession with
            {
                Provider = matchedProvider.Name,
                Model = matchedModel
            };
            if (!string.Equals(matchedSession.Provider, matchedUpdated.Provider, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(matchedSession.Model, matchedUpdated.Model, StringComparison.Ordinal))
            {
                await _conversationStore.SaveAsync(matchedUpdated, cancellationToken);
            }

            return matchedUpdated;
        }

        // Handle --continue
        if (options.Continue)
        {
            var mostRecent = await _conversationStore.GetMostRecentAsync(cancellationToken);
            if (mostRecent is null)
            {
                throw new ConversationStoreException("No sessions found to continue. Create a session first with 'session new' or start a new conversation.");
            }

            var provider = await _providerCatalog.ResolveAsync(
                string.IsNullOrWhiteSpace(options.Provider) ? mostRecent.Provider : options.Provider,
                cancellationToken);
            var resolvedModel = ResolveModel(mostRecent, options.Model, provider.Name, provider.DefaultModel);
            var updated = mostRecent with
            {
                Provider = provider.Name,
                Model = resolvedModel
            };
            if (!string.Equals(mostRecent.Provider, updated.Provider, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(mostRecent.Model, updated.Model, StringComparison.Ordinal))
            {
                await _conversationStore.SaveAsync(updated, cancellationToken);
            }

            return updated;
        }

        // Existing --session logic
        if (!string.IsNullOrWhiteSpace(options.SessionId))
        {
            var existing = await _conversationStore.GetAsync(options.SessionId, cancellationToken);
            if (existing is null)
            {
                throw new ConversationStoreException($"Session '{options.SessionId}' was not found.");
            }

            var provider = await _providerCatalog.ResolveAsync(
                string.IsNullOrWhiteSpace(options.Provider) ? existing.Provider : options.Provider,
                cancellationToken);
            var resolvedModel = ResolveModel(existing, options.Model, provider.Name, provider.DefaultModel);
            var updated = existing with
            {
                Provider = provider.Name,
                Model = resolvedModel
            };
            if (!string.Equals(existing.Provider, updated.Provider, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(existing.Model, updated.Model, StringComparison.Ordinal))
            {
                await _conversationStore.SaveAsync(updated, cancellationToken);
            }

            return updated;
        }

        // Default: create a new session
        var resolvedProvider = await _providerCatalog.ResolveAsync(options.Provider, cancellationToken);
        var model = !string.IsNullOrWhiteSpace(options.Model)
            ? options.Model!
            : !string.IsNullOrWhiteSpace(resolvedProvider.DefaultModel)
                ? resolvedProvider.DefaultModel!
                : throw new ModelProviderConfigurationException(resolvedProvider.Name, "Model must be specified because the provider has no default model configured.");
        return await _conversationStore.CreateAsync("TUI session", model, cancellationToken, resolvedProvider.Name);
    }

    private async Task<ConversationSession> UpdateSessionProviderAsync(
        ConversationSession session,
        string providerName,
        string? model,
        CancellationToken cancellationToken)
    {
        var provider = await _providerCatalog.ResolveAsync(providerName, cancellationToken);
        var resolvedModel = ResolveModel(session, model, provider.Name, provider.DefaultModel);
        var updated = session with
        {
            Provider = provider.Name,
            Model = resolvedModel
        };
        await _conversationStore.SaveAsync(updated, cancellationToken);
        return updated;
    }

    private async Task<PlatformLaunchResult> OpenFromSlashCommandAsync(string args, CancellationToken cancellationToken)
    {
        var parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return new PlatformLaunchResult(false, string.Empty, "Path is required.");
        }

        int? line = null;
        int? column = null;
        var pathParts = parts.ToList();
        if (pathParts.Count > 0 && int.TryParse(pathParts[^1], out var parsedColumn))
        {
            column = parsedColumn;
            pathParts.RemoveAt(pathParts.Count - 1);
        }

        if (pathParts.Count > 0 && int.TryParse(pathParts[^1], out var parsedLine))
        {
            line = parsedLine;
            pathParts.RemoveAt(pathParts.Count - 1);
        }

        var path = string.Join(' ', pathParts).Trim();
        return await _platformLauncher.OpenPathAsync(new PlatformOpenRequest(path, line, column), cancellationToken);
    }

    private static string ResolveModel(ConversationSession session, string? requestedModel, string providerName, string? defaultModel)
    {
        if (!string.IsNullOrWhiteSpace(requestedModel))
        {
            return requestedModel!;
        }

        if (string.Equals(session.Provider, providerName, StringComparison.OrdinalIgnoreCase))
        {
            return session.Model;
        }

        if (!string.IsNullOrWhiteSpace(defaultModel))
        {
            return defaultModel!;
        }

        throw new ModelProviderConfigurationException(providerName, "Model must be specified because the provider has no default model configured.");
    }

    private void Render(bool clearScreen)
    {
        if (_currentSession is null)
        {
            return;
        }

        // If full-screen PTY mode is active, update the output and render
        if (_ptyFullScreenSessionId is not null)
        {
            RenderPtyFullScreenFrame();
            return;
        }

        var size = _terminalSession.GetTerminalSize();
        var layout = new TuiLayoutState(size.Width, size.Height, Math.Max(50, size.Width - 32), Math.Min(30, Math.Max(24, size.Width / 4)), Math.Min(42, Math.Max(30, size.Width / 3)));
        var state = new TuiState(
            _currentSession,
            _currentPermissionMode,
            GetTranscriptMessages(_currentSession),
            GetVisibleTasks(),
            _activityFeed.TakeLast(12).Reverse().ToArray(),
            _promptBuffer,
            _focus,
            _drawer,
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

    private void RenderPtyFullScreenFrame()
    {
        var sessionId = _ptyFullScreenSessionId;
        if (sessionId is null) return;

        var ptyState = _ptyManager.State;
        var session = ptyState.Sessions.FirstOrDefault(s => s.SessionId == sessionId);
        if (session is null)
        {
            // Session no longer exists, exit full-screen mode
            ExitPtyFullScreen();
            Render(clearScreen: true);
            return;
        }

        // Read latest output
        string output;
        lock (_ptyOutputLock)
        {
            output = _ptyFullScreenOutput;
        }

        // If session has exited, auto-exit full-screen mode
        if (!session.IsRunning)
        {
            ExitPtyFullScreen();
            _activityState = TerminalActivityState.Ready;
            _activityDetail = $"PTY session '{sessionId}' has exited.";
            Render(clearScreen: true);
            return;
        }

        var fullScreenState = new PtyFullScreenState(
            session.SessionId,
            session.Command,
            session.IsRunning,
            output,
            $"Running | Duration: {FormatDuration(session.Duration)} | Lines: {session.OutputLineCount} | Scroll: {(_ptyFullScreenScrollOffset > 0 ? $"{_ptyFullScreenScrollOffset} lines up" : "live")}",
            _ptyFullScreenScrollOffset,
            output.Length);

        var state = new TuiState(
            _currentSession!,
            _currentPermissionMode,
            Array.Empty<ConversationMessage>(),
            Array.Empty<TaskRecord>(),
            Array.Empty<string>(),
            string.Empty,
            TuiFocusTarget.PtyFullScreen,
            null,
            null,
            new TuiLayoutState(_terminalSession.GetTerminalSize().Width, _terminalSession.GetTerminalSize().Height, 0, 0, 0),
            _transcriptViewport,
            _contextViewport,
            PtyFullScreen: fullScreenState);

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
        var order = _drawer is null
            ? new[] { TuiFocusTarget.Transcript, TuiFocusTarget.Composer, TuiFocusTarget.Context }
            : new[] { TuiFocusTarget.Transcript, TuiFocusTarget.Composer, TuiFocusTarget.Context, TuiFocusTarget.Drawer };
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
            "Conversation-first shell with drawers for sessions, tasks, PTY, and runtime activity.",
            [
                new TuiOverlaySection(
                    "Keys",
                    [
                        "Tab/Shift+Tab: cycle focus",
                        "PgUp/PgDn: scroll focused pane",
                        "End: jump focused pane to bottom",
                        "F1: help overlay",
                        "F2: sessions drawer",
                        "F3: PTY drawer",
                        "F4: tasks drawer",
                        "F5: activity drawer",
                        "F6/F7: previous/next drawer item",
                        "F8: open selected drawer detail",
                        "Esc: dismiss drawer or overlay",
                        "Ctrl+C: interrupt PTY or active turn"
                    ]),
                new TuiOverlaySection(
                    "Slash Commands",
                    [
                        "/help",
                        "/session",
                        "/provider",
                        "/provider <name> [model]",
                        "/permissions",
                        "/config",
                        "/tasks",
                        "/tasks <id>",
                        "/pty",
                        "/pty <id>",
                        "/pty close <id>",
                        "/pty close-exited",
                        "/activity",
                        "/open <path> [line] [column]",
                        "/browse <url>",
                        "/clear",
                        "/bottom",
                        "/exit"
                    ])
            ]);
        _focus = TuiFocusTarget.Overlay;
    }

    private void ShowPermissionsOverlay()
    {
        var modeDescription = _currentPermissionMode switch
        {
            PermissionMode.Default => "Read-only tools are allowed automatically. Write and execute tools require approval.",
            PermissionMode.AcceptEdits => "Read-only and write tools are allowed automatically. Execute tools require approval.",
            PermissionMode.BypassPermissions => "All tools are allowed automatically. No approval prompts will appear.",
            _ => "Unknown permission mode."
        };

        var readOnlyCount = _toolRegistry.Tools.Count(t => t.Category == ToolCategory.ReadOnly);
        var writeCount = _toolRegistry.Tools.Count(t => t.Category == ToolCategory.Write);
        var executeCount = _toolRegistry.Tools.Count(t => t.Category == ToolCategory.Execute);

        var sections = new List<TuiOverlaySection>
        {
            new TuiOverlaySection("Current mode", [modeDescription]),
            new TuiOverlaySection("Tool categories", [
                $"  ReadOnly: {readOnlyCount} (auto-allowed)",
                $"  Write:    {writeCount} ({(_currentPermissionMode == PermissionMode.AcceptEdits || _currentPermissionMode == PermissionMode.BypassPermissions ? "auto-allowed" : "requires approval")})",
                $"  Execute:  {executeCount} ({(_currentPermissionMode == PermissionMode.BypassPermissions ? "auto-allowed" : "requires approval")})"
            ])
        };

        if (_currentPermissionMode == PermissionMode.BypassPermissions)
        {
            sections.Add(new TuiOverlaySection("Warning", [
                "BYPASS-PERMISSIONS MODE: All tools execute without approval.",
                "Only use this mode in trusted environments with safe inputs."
            ]));
        }

        _overlay = new TuiOverlayState(
            TuiOverlayKind.Permissions,
            "Permissions",
            $"Mode: {_currentPermissionMode.ToString().ToLowerInvariant()}",
            sections);
        _focus = TuiFocusTarget.Overlay;
    }

    private void ShowConfigOverlay()
    {
        var configLines = new List<string>
        {
            $"  Provider:      {_currentSession?.Provider ?? "(none)"}",
            $"  Model:         {_currentSession?.Model ?? "(none)"}",
            $"  Session:       {_currentSession?.Id ?? "(none)"}",
            $"  Permission:    {_currentPermissionMode.ToString().ToLowerInvariant()}"
        };

        var sections = new List<TuiOverlaySection>
        {
            new TuiOverlaySection("Current session config", configLines)
        };

        _overlay = new TuiOverlayState(
            TuiOverlayKind.Config,
            "Configuration",
            "Active session and runtime configuration",
            sections);
        _focus = TuiFocusTarget.Overlay;
    }

    private async Task ToggleSessionDrawerAsync(ReplLaunchOptions options, CancellationToken cancellationToken)
    {
        if (_drawer?.Kind == TuiDrawerKind.Sessions)
        {
            _drawer = null;
            _focus = TuiFocusTarget.Composer;
            return;
        }

        await ShowSessionDrawerAsync(_selectedSessionId ?? _currentSession?.Id ?? options.SessionId, cancellationToken);
    }

    private async Task ToggleTasksDrawerAsync(CancellationToken cancellationToken)
    {
        if (_drawer?.Kind == TuiDrawerKind.Tasks)
        {
            _drawer = null;
            _focus = TuiFocusTarget.Composer;
            return;
        }

        await ShowTaskDrawerAsync(_selectedTaskId, cancellationToken);
    }

    private async Task ShowSessionDrawerAsync(string? sessionId, CancellationToken cancellationToken)
    {
        var sessions = await _conversationStore.ListAsync(cancellationToken);
        var selected = !string.IsNullOrWhiteSpace(sessionId)
            ? sessions.FirstOrDefault(session => string.Equals(session.Id, sessionId, StringComparison.Ordinal))
            : _currentSession;
        if (selected is not null)
        {
            _selectedSessionId = selected.Id;
        }

        var items = sessions
            .Take(12)
            .Select(session => new TuiDrawerItem(
                session.Id,
                $"{session.Title ?? "Session"} ({session.Id})",
                $"provider={session.Provider} | model={session.Model} | messages={session.Messages.Count} | updated={session.UpdatedAtUtc:HH:mm:ss}",
                _currentSession is not null && string.Equals(_currentSession.Id, session.Id, StringComparison.Ordinal),
                selected is not null && string.Equals(selected.Id, session.Id, StringComparison.Ordinal)))
            .ToArray();
        var detail = selected is null
            ? ["No session selected."]
            : BuildSessionDetail(selected);
        _drawer = new TuiDrawerState(
            TuiDrawerKind.Sessions,
            "Sessions",
            items,
            selected is null ? "Session detail" : $"Session detail: {selected.Id}",
            detail);
        _focus = TuiFocusTarget.Drawer;
    }

    private async Task ShowTaskDrawerAsync(string? taskId, CancellationToken cancellationToken)
    {
        var tasks = await _taskManager.ListAsync(cancellationToken);
        var selected = !string.IsNullOrWhiteSpace(taskId)
            ? tasks.FirstOrDefault(task => string.Equals(task.Id, taskId, StringComparison.Ordinal))
            : tasks.FirstOrDefault();
        if (selected is not null)
        {
            _selectedTaskId = selected.Id;
        }

        var items = tasks
            .Take(10)
            .Select(task => new TuiDrawerItem(
                task.Id,
                $"{task.Title} ({task.Id})",
                $"{task.Status} | depth={task.Depth} | parent={(task.ParentTaskId ?? "root")} | children={task.ChildTaskIds?.Count ?? 0} | updated={task.UpdatedAtUtc:HH:mm:ss}",
                task.Status == ClawdNet.Core.Models.TaskStatus.Running,
                selected is not null && string.Equals(selected.Id, task.Id, StringComparison.Ordinal)))
            .ToArray();
        var detail = await BuildTaskDetailAsync(selected?.Id, cancellationToken);
        _drawer = new TuiDrawerState(
            TuiDrawerKind.Tasks,
            "Tasks",
            items,
            selected is null ? "Task detail" : $"Task detail: {selected.Id}",
            detail);
        _focus = TuiFocusTarget.Drawer;
    }

    private async Task TogglePtyDrawerAsync(CancellationToken cancellationToken)
    {
        if (_drawer?.Kind == TuiDrawerKind.Pty)
        {
            _drawer = null;
            _focus = TuiFocusTarget.Composer;
            return;
        }

        await ShowPtyDrawerAsync(_selectedPtySessionId ?? _ptyManager.State.CurrentSessionId, focusSession: false, cancellationToken);
    }

    private void ToggleActivityDrawer()
    {
        if (_drawer?.Kind == TuiDrawerKind.Activity)
        {
            _drawer = null;
            _focus = TuiFocusTarget.Composer;
            return;
        }

        _drawer = new TuiDrawerState(
            TuiDrawerKind.Activity,
            "Runtime activity",
            _activityFeed.Take(20).Select((line, index) => new TuiDrawerItem(index.ToString(), line, null, false, index == 0)).ToArray(),
            "Recent activity",
            _activityFeed.Take(20).ToArray());
        _focus = TuiFocusTarget.Drawer;
    }

    private async Task ShowPtyDrawerAsync(string? sessionId, bool focusSession, CancellationToken cancellationToken)
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
        var selected = !string.IsNullOrWhiteSpace(sessionId)
            ? state.Sessions.FirstOrDefault(session => string.Equals(session.SessionId, sessionId, StringComparison.Ordinal))
            : state.Sessions.FirstOrDefault(session => session.IsCurrent) ?? state.Sessions.FirstOrDefault();
        if (selected is not null)
        {
            _selectedPtySessionId = selected.SessionId;
        }

        var items = state.Sessions
            .Take(10)
            .Select(session =>
            {
                var duration = FormatDuration(session.Duration);
                var status = session.IsRunning ? "running" : "stopped";
                var backgroundTag = session.IsBackground ? " [bg]" : "";
                var lineInfo = session.OutputLineCount > 0 ? $" | {session.OutputLineCount} lines" : "";
                var timeoutWarning = session.Timeout.HasValue && session.IsRunning && session.Duration > session.Timeout.Value * 0.8 ? " ⚠️" : "";
                return new TuiDrawerItem(
                    session.SessionId,
                    $"{session.Command}{backgroundTag} ({session.SessionId})",
                    $"{status} | {duration}{lineInfo} | cwd={session.WorkingDirectory}{timeoutWarning}",
                    session.IsCurrent,
                    selected is not null && string.Equals(selected.SessionId, session.SessionId, StringComparison.Ordinal));
            })
            .ToArray();
        var detail = selected is null
            ? ["No PTY session selected."]
            : await BuildPtyDetailAsync(selected.SessionId, cancellationToken);
        _drawer = new TuiDrawerState(
            TuiDrawerKind.Pty,
            "PTY sessions",
            items,
            selected is null ? "PTY detail" : $"PTY detail: {selected.SessionId}",
            detail);
        _focus = TuiFocusTarget.Drawer;
    }

    private async Task RefreshPtyDrawerAsync()
    {
        await ShowPtyDrawerAsync(_selectedPtySessionId ?? _ptyManager.State.CurrentSessionId, focusSession: false, CancellationToken.None);
    }

    private async Task<IReadOnlyList<string>> BuildPtyDetailAsync(string sessionId, CancellationToken cancellationToken)
    {
        var state = await _ptyManager.ReadAsync(sessionId, cancellationToken);
        if (state is null)
        {
            return ["Selected PTY session was not found."];
        }

        var lines = new List<string>
        {
            $"session={state.SessionId}",
            $"command={state.Command}",
            $"cwd={state.WorkingDirectory}",
            $"running={state.IsRunning}",
            $"exitCode={(state.ExitCode.HasValue ? state.ExitCode.Value.ToString() : "n/a")}",
            $"duration={FormatDuration(state.Duration)}",
            $"lines={state.OutputLineCount}"
        };
        if (state.IsBackground)
        {
            lines.Add("background=true");
        }
        if (state.Timeout.HasValue)
        {
            lines.Add($"timeout={FormatDuration(state.Timeout.Value)}");
        }
        if (state.IsOutputClipped)
        {
            lines.Add("outputClipped=true");
        }

        lines.Add(string.Empty);
        lines.Add(string.IsNullOrWhiteSpace(state.RecentOutput) ? "(no output yet)" : state.RecentOutput.TrimEnd());
        lines.Add(string.Empty);
        lines.Add("commands: /pty <id> | /pty close <id> | /pty close-exited | /pty status <id>");
        return lines;
    }

    private static string FormatDuration(TimeSpan ts)
    {
        if (ts.TotalMinutes < 1)
        {
            return $"{(int)ts.TotalSeconds}s";
        }
        if (ts.TotalHours < 1)
        {
            return $"{(int)ts.TotalMinutes}m{(int)ts.Seconds}s";
        }
        return $"{(int)ts.TotalHours}h{(int)ts.Minutes}m";
    }

    private void HandlePtyStateChanged(PtyManagerState state)
    {
        if (_currentSession is null)
        {
            return;
        }

        // Update full-screen PTY output if active
        if (_ptyFullScreenSessionId is not null && state.CurrentSession is not null)
        {
            if (string.Equals(state.CurrentSession.SessionId, _ptyFullScreenSessionId, StringComparison.Ordinal))
            {
                lock (_ptyOutputLock)
                {
                    _ptyFullScreenOutput = state.CurrentSession.RecentOutput;
                }
            }
        }

        if (state.CurrentSession is not null && state.CurrentSession.IsRunning)
        {
            _activityState = TerminalActivityState.RunningTool;
            _activityDetail = $"PTY session active: {state.CurrentSession.Command}";
        }

        if (_drawer?.Kind == TuiDrawerKind.Pty)
        {
            _ = RefreshPtyDrawerAsync();
        }

        AddActivityFeed($"pty | current={state.CurrentSessionId ?? "none"}");
        MarkContextLiveUpdate();
        Render(clearScreen: true);
    }

    private async Task EnterPtyFullScreenAsync(string sessionId, CancellationToken cancellationToken)
    {
        var session = _ptyManager.State.Sessions.FirstOrDefault(s => s.SessionId == sessionId);
        if (session is null || !session.IsRunning)
        {
            _activityState = TerminalActivityState.Error;
            _activityDetail = $"PTY session '{sessionId}' is not running.";
            return;
        }

        _ptyFullScreenSessionId = sessionId;
        _ptyFullScreenOutput = string.Empty;
        _focus = TuiFocusTarget.PtyFullScreen;

        // Start streaming output from the PTY session
        StartPtyOutputStreaming(sessionId, cancellationToken);

        _activityState = TerminalActivityState.RunningTool;
        _activityDetail = $"Full-screen PTY: {session.Command}";
        AddActivityFeed($"pty | fullscreen | {sessionId}");
        Render(clearScreen: true);
    }

    private void ExitPtyFullScreen()
    {
        _ptyFullScreenSessionId = null;
        _ptyFullScreenOutput = string.Empty;
        _focus = TuiFocusTarget.Composer;
        _activityState = TerminalActivityState.Ready;
        _activityDetail = "Exited full-screen PTY mode.";
        AddActivityFeed("pty | fullscreen-exit");
    }

    private async Task HandlePtyFullScreenInputAsync(CancellationToken cancellationToken)
    {
        var promptResult = await _terminalSession.ReadPromptAsync("", string.Empty, cancellationToken);
        if (promptResult.Kind == PromptInputKind.EndOfStream)
        {
            ExitPtyFullScreen();
            _activityState = TerminalActivityState.Exiting;
            _activityDetail = "Exiting ClawdNet.";
            Render(clearScreen: true);
            return;
        }

        // Handle Esc to exit full-screen mode (only when buffer is empty or at bottom)
        if (promptResult.Text == "\x1b")
        {
            ExitPtyFullScreen();
            Render(clearScreen: true);
            return;
        }

        // Handle special keys for scrolling
        if (promptResult.Kind == PromptInputKind.ScrollPageUp)
        {
            _ptyFullScreenScrollOffset = Math.Max(0, _ptyFullScreenScrollOffset - 10);
            Render(clearScreen: false);
            return;
        }

        if (promptResult.Kind == PromptInputKind.ScrollPageDown)
        {
            _ptyFullScreenScrollOffset += 10;
            Render(clearScreen: false);
            return;
        }

        if (promptResult.Kind == PromptInputKind.ScrollBottom)
        {
            _ptyFullScreenScrollOffset = 0; // 0 means follow live output
            Render(clearScreen: false);
            return;
        }

        // Map special keys to ANSI escape sequences
        string? escapeSequence = promptResult.Text switch
        {
            // Arrow keys
            "\x1b[A" => "\x1b[A",  // Up
            "\x1b[B" => "\x1b[B",  // Down
            "\x1b[C" => "\x1b[C",  // Right
            "\x1b[D" => "\x1b[D",  // Left
            // Home/End
            "\x1b[H" => "\x1b[H",  // Home
            "\x1b[F" => "\x1b[F",  // End
            "\x1b[1~" => "\x1b[H", // Home (alternate)
            "\x1b[4~" => "\x1b[F", // End (alternate)
            // Function keys (F1-F12)
            "\x1b[11~" => "\x1b[11~", // F1
            "\x1b[12~" => "\x1b[12~", // F2
            "\x1b[13~" => "\x1b[13~", // F3
            "\x1b[14~" => "\x1b[14~", // F4
            "\x1b[15~" => "\x1b[15~", // F5
            // Tab and Backspace
            "\t" => "\t",
            "\x7f" => "\x7f", // Backspace
            "\b" => "\x7f",   // Backspace (alternate)
            _ => null
        };

        if (escapeSequence is not null)
        {
            // Forward escape sequence to PTY
            if (_ptyFullScreenSessionId is not null)
            {
                try
                {
                    await _ptyManager.WriteAsync(escapeSequence, _ptyFullScreenSessionId, cancellationToken);
                }
                catch (Exception ex)
                {
                    _activityState = TerminalActivityState.Error;
                    _activityDetail = $"PTY write failed: {ex.Message}";
                    ExitPtyFullScreen();
                    Render(clearScreen: true);
                }
            }
            return;
        }

        // Forward all other input to the PTY session
        if (!string.IsNullOrEmpty(promptResult.Text) && _ptyFullScreenSessionId is not null)
        {
            try
            {
                await _ptyManager.WriteAsync(promptResult.Text, _ptyFullScreenSessionId, cancellationToken);
            }
            catch (Exception ex)
            {
                _activityState = TerminalActivityState.Error;
                _activityDetail = $"PTY write failed: {ex.Message}";
                ExitPtyFullScreen();
                Render(clearScreen: true);
            }
        }
    }

    private async void StartPtyOutputStreaming(string sessionId, CancellationToken cancellationToken)
    {
        try
        {
            var session = _ptyManager.State.Sessions.FirstOrDefault(s => s.SessionId == sessionId);
            if (session is null) return;

            // Get recent output and stream new output
            var output = await _ptyManager.ReadAsync(sessionId, cancellationToken);
            if (output is not null)
            {
                lock (_ptyOutputLock)
                {
                    _ptyFullScreenOutput = output.RecentOutput;
                }
            }

            // Output updates will be handled via PtyStateChanged handler
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"PTY streaming error: {ex.Message}");
        }
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
            AddActivityFeed($"task | {task.Id} | {task.Status} | {taskEvent.Message}");

            // Add aggregate orchestration status
            var allTasks = await _taskManager.ListAsync(CancellationToken.None);
            var sessionTasks = allTasks.Where(t => string.Equals(t.ParentSessionId, task.ParentSessionId, StringComparison.Ordinal)).ToArray();
            var running = sessionTasks.Count(t => t.Status == ClawdNet.Core.Models.TaskStatus.Running);
            var pending = sessionTasks.Count(t => t.Status == ClawdNet.Core.Models.TaskStatus.Pending);
            var completed = sessionTasks.Count(t => t.Status == ClawdNet.Core.Models.TaskStatus.Completed);
            if (running > 0 || pending > 0)
            {
                AddActivityFeed($"orchestration | running={running} pending={pending} completed={completed}");
            }

            if (_drawer?.Kind == TuiDrawerKind.Tasks)
            {
                await ShowTaskDrawerAsync(_selectedTaskId ?? task.Id, CancellationToken.None);
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

    private void AddActivityFeed(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        _activityFeed.Insert(0, $"[{DateTimeOffset.UtcNow:HH:mm:ss}] {message}");
        if (_activityFeed.Count > 40)
        {
            _activityFeed.RemoveRange(40, _activityFeed.Count - 40);
        }

        if (_drawer?.Kind == TuiDrawerKind.Activity)
        {
            ToggleActivityDrawer();
            ToggleActivityDrawer();
        }
    }

    private async Task MoveDrawerSelectionAsync(int delta, CancellationToken cancellationToken)
    {
        if (_drawer is null || _drawer.Items.Count == 0)
        {
            return;
        }

        var currentIndex = _drawer.Items.ToList().FindIndex(item => item.IsSelected);
        if (currentIndex < 0)
        {
            currentIndex = 0;
        }

        var nextIndex = Math.Clamp(currentIndex + delta, 0, _drawer.Items.Count - 1);
        var selectedId = _drawer.Items[nextIndex].Id;
        switch (_drawer.Kind)
        {
            case TuiDrawerKind.Sessions:
                await ShowSessionDrawerAsync(selectedId, cancellationToken);
                break;
            case TuiDrawerKind.Tasks:
                await ShowTaskDrawerAsync(selectedId, cancellationToken);
                break;
            case TuiDrawerKind.Pty:
                await ShowPtyDrawerAsync(selectedId, focusSession: false, cancellationToken);
                break;
            case TuiDrawerKind.Activity:
                ToggleActivityDrawer();
                break;
        }
    }

    private async Task OpenSelectedDrawerItemAsync(ReplLaunchOptions options, CancellationToken cancellationToken)
    {
        if (_drawer is null)
        {
            return;
        }

        var selectedId = _drawer.Items.FirstOrDefault(item => item.IsSelected)?.Id;
        switch (_drawer.Kind)
        {
            case TuiDrawerKind.Sessions:
                if (!string.IsNullOrWhiteSpace(selectedId))
                {
                    await ShowSessionDrawerAsync(selectedId, cancellationToken);
                }
                break;
            case TuiDrawerKind.Tasks:
                if (!string.IsNullOrWhiteSpace(selectedId))
                {
                    await ShowTaskDrawerAsync(selectedId, cancellationToken);
                }
                break;
            case TuiDrawerKind.Pty:
                if (!string.IsNullOrWhiteSpace(selectedId))
                {
                    await ShowPtyDrawerAsync(selectedId, focusSession: true, cancellationToken);
                }
                break;
            case TuiDrawerKind.Activity:
                await ToggleSessionDrawerAsync(options, cancellationToken);
                break;
        }
    }

    private void DismissSurface()
    {
        if (_overlay is not null)
        {
            _overlay = null;
            _focus = _drawer is null ? TuiFocusTarget.Composer : TuiFocusTarget.Drawer;
            return;
        }

        if (_drawer is not null)
        {
            _drawer = null;
            _focus = TuiFocusTarget.Composer;
        }
    }

    private IReadOnlyList<string> BuildSessionDetail(ConversationSession session)
    {
        var tail = session.Messages.TakeLast(8)
            .Select(message => $"{message.Role}: {message.Content}")
            .ToArray();
        return
        [
            $"session={session.Id}",
            $"title={session.Title ?? "(untitled)"}",
            $"provider={session.Provider}",
            $"model={session.Model}",
            $"messages={session.Messages.Count}",
            $"updated={session.UpdatedAtUtc:O}",
            string.Empty,
            "Transcript tail:",
            .. tail
        ];
    }

    private async Task<IReadOnlyList<string>> BuildTaskDetailAsync(string? taskId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(taskId))
        {
            return ["No task selected."];
        }

        var inspection = await _taskManager.InspectAsync(taskId, cancellationToken);
        if (inspection is null)
        {
            return [$"Task '{taskId}' was not found."];
        }

        var lines = new List<string>
        {
            $"task={inspection.Task.Id}",
            $"status={inspection.Task.Status}",
            $"title={inspection.Task.Title}",
            $"parentTask={inspection.Task.ParentTaskId ?? "(root)"}",
            $"rootTask={inspection.Task.RootTaskId ?? inspection.Task.Id}",
            $"depth={inspection.Task.Depth}",
            $"childTasks={inspection.Children.Count}",
            $"dependsOn={string.Join(", ", inspection.Task.DependsOnTaskIds ?? Array.Empty<string>())}",
            $"workerSession={inspection.Worker.WorkerSessionId}",
            $"workerMessages={inspection.Worker.MessageCount}",
            $"updated={inspection.Worker.UpdatedAtUtc:O}",
            $"summary={inspection.Task.Result?.Summary ?? inspection.Task.LastStatusMessage ?? "(none)"}",
            string.Empty,
            "Recent events:"
        };
        lines.AddRange(inspection.RecentEvents.Take(8).Select(taskEvent => $"{taskEvent.TimestampUtc:HH:mm:ss} | {taskEvent.Status} | {taskEvent.Message}"));
        lines.Add(string.Empty);
        lines.Add("Child tasks:");
        if (inspection.Children.Count == 0)
        {
            lines.Add("(none)");
        }
        else
        {
            lines.AddRange(inspection.Children.Select(child => $"{child.Id} | {child.Status} | {child.Title} | updated={child.UpdatedAtUtc:HH:mm:ss}"));
        }
        lines.Add(string.Empty);
        lines.Add("Worker transcript:");
        lines.Add(string.IsNullOrWhiteSpace(inspection.Worker.TranscriptTail) ? "(none)" : inspection.Worker.TranscriptTail);
        return lines;
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
                [
                    new TuiOverlaySection("Tool", [$"name={toolCall.Name}"]),
                    new TuiOverlaySection("Reason", [permissionDecision.Reason])
                ],
                true);
            _owner._focus = TuiFocusTarget.Overlay;
            _owner._activityState = TerminalActivityState.AwaitingApproval;
            _owner._activityDetail = permissionDecision.Reason;
            _owner.AddActivityFeed($"approval | requested | {toolCall.Name}");
            _owner.Render(clearScreen: true);
            var approved = await _owner._terminalSession.ConfirmAsync($"Approve {toolCall.Name}?", cancellationToken);
            _owner._overlay = null;
            _owner._focus = TuiFocusTarget.Composer;
            _owner.AddActivityFeed($"approval | {(approved ? "approved" : "denied")} | {toolCall.Name}");
            return approved;
        }
    }

    private async Task ShowSessionStatusAsync(CancellationToken cancellationToken)
    {
        if (_currentSession is null)
        {
            _activityState = TerminalActivityState.Error;
            _activityDetail = "No active session.";
            return;
        }

        var tasks = await _taskManager.ListAsync(cancellationToken);
        var sessionTasks = tasks.Where(t => string.Equals(t.ParentSessionId, _currentSession.Id, StringComparison.Ordinal)).ToArray();
        var runningTasks = sessionTasks.Count(t => t.Status == ClawdNet.Core.Models.TaskStatus.Running);
        var completedTasks = sessionTasks.Count(t => t.Status == ClawdNet.Core.Models.TaskStatus.Completed);

        var lines = new List<string>
        {
            $"session={_currentSession.Id}",
            $"title={_currentSession.Title}",
            $"provider={_currentSession.Provider}",
            $"model={_currentSession.Model}",
            $"permissionMode={_currentPermissionMode}",
            $"messages={_currentSession.Messages.Count}",
            $"tasks={sessionTasks.Length} (running={runningTasks}, completed={completedTasks})",
            $"created={_currentSession.CreatedAtUtc:O}",
            $"updated={_currentSession.UpdatedAtUtc:O}"
        };

        _activityState = TerminalActivityState.ShowingSession;
        _activityDetail = $"Session status: {_currentSession.Title} | {_currentSession.Provider}/{_currentSession.Model}";
        _overlay = new TuiOverlayState(TuiOverlayKind.Session, "Session Status", _activityDetail,
            [new TuiOverlaySection("Status", lines)]);
        _focus = TuiFocusTarget.Overlay;
        Render(clearScreen: true);
    }

    private Task ShowSessionContextAsync(CancellationToken cancellationToken)
    {
        if (_currentSession is null)
        {
            _activityState = TerminalActivityState.Error;
            _activityDetail = "No active session.";
            return Task.CompletedTask;
        }

        var userMessages = _currentSession.Messages.Count(m => m.Role == "user");
        var assistantMessages = _currentSession.Messages.Count(m => m.Role == "assistant");
        var toolMessages = _currentSession.Messages.Count(m => m.Role.StartsWith("tool_") || m.Role == "task_started" || m.Role == "task_completed");

        var lines = new List<string>
        {
            $"session={_currentSession.Id}",
            $"provider={_currentSession.Provider}",
            $"model={_currentSession.Model}",
            $"permissionMode={_currentPermissionMode}",
            $"userMessages={userMessages}",
            $"assistantMessages={assistantMessages}",
            $"systemMessages={toolMessages}",
            $"totalMessages={_currentSession.Messages.Count}"
        };

        _activityState = TerminalActivityState.ShowingSession;
        _activityDetail = $"Context: {_currentSession.Messages.Count} messages | {userMessages} user, {assistantMessages} assistant";
        _overlay = new TuiOverlayState(TuiOverlayKind.Session, "Session Context", _activityDetail,
            [new TuiOverlaySection("Context", lines)]);
        _focus = TuiFocusTarget.Overlay;
        Render(clearScreen: true);
        return Task.CompletedTask;
    }

    private async Task ShowPtyStatusAsync(string ptySessionId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ptySessionId))
        {
            _activityState = TerminalActivityState.Error;
            _activityDetail = "PTY session ID is required.";
            return;
        }

        var sessions = await _ptyManager.ListAsync(cancellationToken);
        var session = sessions.FirstOrDefault(s => string.Equals(s.SessionId, ptySessionId, StringComparison.Ordinal));
        if (session is null)
        {
            _activityState = TerminalActivityState.Error;
            _activityDetail = $"PTY session '{ptySessionId}' was not found.";
            return;
        }

        var lines = new List<string>
        {
            $"sessionId={session.SessionId}",
            $"command={session.Command}",
            $"workingDirectory={session.WorkingDirectory}",
            $"running={session.IsRunning}",
            $"exited={(!session.IsRunning)}",
            $"exitCode={session.ExitCode?.ToString() ?? "(running)"}",
            $"outputClipped={session.IsOutputClipped}",
            $"startedAt={session.StartedAtUtc:O}",
            $"updatedAt={session.UpdatedAtUtc:O}"
        };

        _activityState = TerminalActivityState.ShowingSession;
        _activityDetail = $"PTY session: {session.Command} | {(session.IsRunning ? "running" : "stopped")}";
        _overlay = new TuiOverlayState(TuiOverlayKind.Session, "PTY Session Status", _activityDetail,
            [new TuiOverlaySection("Status", lines)]);
        _focus = TuiFocusTarget.Overlay;
        Render(clearScreen: true);
    }
}
