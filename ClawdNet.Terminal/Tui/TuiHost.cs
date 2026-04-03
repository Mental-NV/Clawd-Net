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

    public TuiHost(
        ITerminalSession terminalSession,
        IConversationStore conversationStore,
        IQueryEngine queryEngine,
        ITuiRenderer tuiRenderer,
        IPtyManager ptyManager,
        ITaskManager taskManager,
        IProviderCatalog? providerCatalog = null,
        IPlatformLauncher? platformLauncher = null)
    {
        _terminalSession = terminalSession;
        _conversationStore = conversationStore;
        _queryEngine = queryEngine;
        _tuiRenderer = tuiRenderer;
        _ptyManager = ptyManager;
        _taskManager = taskManager;
        _providerCatalog = providerCatalog ?? new TerminalFallbackProviderCatalog();
        _platformLauncher = platformLauncher ?? new TerminalNullPlatformLauncher();
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
        _promptBuffer = string.Empty;
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
            .Select(session => new TuiDrawerItem(
                session.SessionId,
                $"{session.Command} ({session.SessionId})",
                $"{(session.IsRunning ? "running" : "stopped")} | cwd={session.WorkingDirectory}",
                session.IsCurrent,
                selected is not null && string.Equals(selected.SessionId, session.SessionId, StringComparison.Ordinal)))
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

        return
        [
            $"session={state.SessionId}",
            $"command={state.Command}",
            $"cwd={state.WorkingDirectory}",
            $"running={state.IsRunning}",
            $"exitCode={(state.ExitCode.HasValue ? state.ExitCode.Value.ToString() : "n/a")}",
            $"outputClipped={state.IsOutputClipped}",
            string.Empty,
            string.IsNullOrWhiteSpace(state.RecentOutput) ? "(no output yet)" : state.RecentOutput.TrimEnd(),
            string.Empty,
            "commands: /pty <id> | /pty close <id> | /pty close-exited"
        ];
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

        if (_drawer?.Kind == TuiDrawerKind.Pty)
        {
            _ = RefreshPtyDrawerAsync();
        }

        AddActivityFeed($"pty | current={state.CurrentSessionId ?? "none"}");
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
            AddActivityFeed($"task | {task.Id} | {taskEvent.Message}");
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
}
