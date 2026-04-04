using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Exceptions;
using ClawdNet.Core.Models;
using ClawdNet.Terminal.Abstractions;
using ClawdNet.Terminal.Defaults;
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
    private readonly IProviderCatalog _providerCatalog;
    private readonly IPlatformLauncher _platformLauncher;
    private readonly IToolRegistry _toolRegistry;
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
        ITaskManager taskManager,
        IProviderCatalog? providerCatalog = null,
        IPlatformLauncher? platformLauncher = null,
        IToolRegistry? toolRegistry = null)
    {
        _terminalSession = terminalSession;
        _conversationStore = conversationStore;
        _queryEngine = queryEngine;
        _transcriptRenderer = transcriptRenderer;
        _ptyManager = ptyManager;
        _taskManager = taskManager;
        _providerCatalog = providerCatalog ?? new TerminalFallbackProviderCatalog();
        _platformLauncher = platformLauncher ?? new TerminalNullPlatformLauncher();
        _toolRegistry = toolRegistry ?? new TerminalFallbackToolRegistry();
        _approvalHandler = new TerminalApprovalHandler(terminalSession, HandleActivityChange);
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
        _visibleStartIndex = 0;
        _viewport = new TerminalViewportState();
        _promptBuffer = string.Empty;
        _promptHistory.ResetNavigation();
        _ptyManager.StateChanged += HandlePtyStateChanged;
        _taskManager.TaskChanged += HandleTaskChanged;
        try
        {
            Render(session, options.PermissionMode, _visibleStartIndex, clearScreen: true);

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
                    if (_currentSession is not null)
                    {
                        session = _currentSession;
                    }
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
                                       new QueryRequest(prompt, session.Id, session.Model, 8, options.PermissionMode, _approvalHandler, true, session.Provider),
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
                            case PluginHookRecordedEvent hookRecorded:
                                session = hookRecorded.Session;
                                _currentSession = session;
                                MarkLiveUpdate();
                                SetActivity(
                                    hookRecorded.Result.Success ? TerminalActivityState.RunningTool : TerminalActivityState.Error,
                                    $"{hookRecorded.Result.Plugin.Name}:{hookRecorded.Result.Hook.Kind} -> {hookRecorded.Result.Message}");
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
                catch (ModelProviderConfigurationException ex)
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
            _ptyManager.StateChanged -= HandlePtyStateChanged;
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
                    "Commands: /help, /session, /provider, /permissions, /config, /tasks, /pty, /open, /browse, /clear, /bottom, /exit. Keys: Up/Down history, PgUp/PgDn scroll, End bottom, F3 PTY overlay in TUI.");
                return true;
            case "/session":
                SetActivity(
                    TerminalActivityState.ShowingSession,
                    $"Session {session.Id} | provider={session.Provider} | model={session.Model} | permission={FormatPermissionMode(options.PermissionMode)} | messages={session.Messages.Count}");
                return true;
            case "/provider":
                SetActivity(
                    TerminalActivityState.ShowingSession,
                    $"Provider {session.Provider} | model={session.Model}");
                return true;
            case "/clear":
                _visibleStartIndex = session.Messages.Count;
                _terminalSession.ClearVisible();
                _viewport = new TerminalViewportState();
                SetActivity(TerminalActivityState.Cleared, "Screen cleared. Session history is preserved.");
                return true;
            case "/pty":
                var ptyState = _ptyManager.State;
                SetActivity(
                    TerminalActivityState.ShowingSession,
                    ptyState.CurrentSession is null
                        ? "No active PTY session."
                        : $"PTY {ptyState.CurrentSession.SessionId} | running={ptyState.CurrentSession.IsRunning} | command={ptyState.CurrentSession.Command} | others={Math.Max(0, ptyState.Sessions.Count - 1)}");
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
            case "/permissions":
                ShowPermissionsInfo();
                return true;
            case "/config":
                ShowConfigInfo();
                return true;
            default:
                if (prompt.StartsWith("/provider ", StringComparison.OrdinalIgnoreCase))
                {
                    var args = prompt["/provider ".Length..].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (args.Length > 0)
                    {
                        try
                        {
                            var updated = await UpdateSessionProviderAsync(session, args[0], args.Length > 1 ? args[1] : null, cancellationToken);
                            _currentSession = updated;
                            SetActivity(TerminalActivityState.ShowingSession, $"Provider updated to {updated.Provider} | model={updated.Model}");
                        }
                        catch (ModelProviderConfigurationException ex)
                        {
                            SetActivity(TerminalActivityState.Error, ex.Message);
                        }

                        return true;
                    }
                }

                if (prompt.StartsWith("/open ", StringComparison.OrdinalIgnoreCase))
                {
                    var result = await OpenFromSlashCommandAsync(prompt["/open ".Length..], cancellationToken);
                    SetActivity(result.Success ? TerminalActivityState.ShowingSession : TerminalActivityState.Error, result.Success ? result.Message : result.Error);
                    return true;
                }

                if (prompt.StartsWith("/browse ", StringComparison.OrdinalIgnoreCase))
                {
                    var url = prompt["/browse ".Length..].Trim();
                    var result = await _platformLauncher.OpenUrlAsync(url, cancellationToken);
                    SetActivity(result.Success ? TerminalActivityState.ShowingSession : TerminalActivityState.Error, result.Success ? result.Message : result.Error);
                    return true;
                }

                return false;
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
        return await _conversationStore.CreateAsync("Interactive session", model, cancellationToken, resolvedProvider.Name);
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
            _ptyManager.State,
            _viewport.FollowLiveOutput,
            _viewport.HasBufferedLiveOutput,
            error);
        var draft = _transcriptRenderer.RenderDraft(_draft);
        var pty = _transcriptRenderer.RenderPty(_ptyManager.State);
        var activity = _transcriptRenderer.RenderActivity(_activityState, _activityDetail);
        _terminalSession.Render(new TerminalViewState("ClawdNet interactive mode", transcript, footer, _promptBuffer, _viewport, draft, pty, activity, clearScreen));
    }

    private void SetActivity(TerminalActivityState state, string? detail)
    {
        _activityState = state;
        _activityDetail = detail;
    }

    private void ShowPermissionsInfo()
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

        var lines = new List<string>
        {
            $"Mode: {_currentPermissionMode.ToString().ToLowerInvariant()}",
            modeDescription,
            string.Empty,
            "Tool categories:",
            $"  ReadOnly: {readOnlyCount} (auto-allowed)",
            $"  Write:    {writeCount} ({(_currentPermissionMode == PermissionMode.AcceptEdits || _currentPermissionMode == PermissionMode.BypassPermissions ? "auto-allowed" : "requires approval")})",
            $"  Execute:  {executeCount} ({(_currentPermissionMode == PermissionMode.BypassPermissions ? "auto-allowed" : "requires approval")})"
        };

        if (_currentPermissionMode == PermissionMode.BypassPermissions)
        {
            lines.Add(string.Empty);
            lines.Add("WARNING: BYPASS-PERMISSIONS MODE - All tools execute without approval.");
            lines.Add("Only use this mode in trusted environments with safe inputs.");
        }

        SetActivity(TerminalActivityState.ShowingSession, string.Join(Environment.NewLine, lines));
        if (_currentSession is not null)
        {
            Render(_currentSession, _currentPermissionMode, _visibleStartIndex, clearScreen: true);
        }
    }

    private void ShowConfigInfo()
    {
        var lines = new List<string>
        {
            "Active session configuration:",
            $"  Provider:   {_currentSession?.Provider ?? "(none)"}",
            $"  Model:      {_currentSession?.Model ?? "(none)"}",
            $"  Session:    {_currentSession?.Id ?? "(none)"}",
            $"  Permission: {_currentPermissionMode.ToString().ToLowerInvariant()}"
        };

        SetActivity(TerminalActivityState.ShowingSession, string.Join(Environment.NewLine, lines));
        if (_currentSession is not null)
        {
            Render(_currentSession, _currentPermissionMode, _visibleStartIndex, clearScreen: true);
        }
    }

    private void HandleActivityChange(TerminalActivityState state, string? detail)
    {
        SetActivity(state, detail);
        if (_currentSession is not null)
        {
            Render(_currentSession, _currentPermissionMode, _visibleStartIndex, clearScreen: true);
        }
    }

    private void HandlePtyStateChanged(PtyManagerState state)
    {
        if (_currentSession is null)
        {
            return;
        }

        MarkLiveUpdate();
        if (state.CurrentSession is not null && state.CurrentSession.IsRunning)
        {
            SetActivity(TerminalActivityState.RunningTool, $"PTY session active: {state.CurrentSession.Command}");
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
