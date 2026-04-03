using System.Collections.Concurrent;
using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Models;
using ClawdNet.Runtime.Plugins;
using ClawdTaskStatus = ClawdNet.Core.Models.TaskStatus;

namespace ClawdNet.Runtime.Tasks;

public sealed class TaskManager : ITaskManager
{
    private const int MaxDelegationDepth = 1;
    private const int MaxHookSummaryLength = 240;
    private const int TaskEventLimit = 12;
    private const int WorkerTranscriptTailLength = 800;
    private readonly ITaskStore _taskStore;
    private readonly IConversationStore _conversationStore;
    private readonly IQueryEngine _queryEngine;
    private readonly IPluginRuntime _pluginRuntime;
    private readonly ConcurrentDictionary<string, RunningTaskHandle> _running = new(StringComparer.Ordinal);

    public TaskManager(
        ITaskStore taskStore,
        IConversationStore conversationStore,
        IQueryEngine queryEngine,
        IPluginRuntime? pluginRuntime = null)
    {
        _taskStore = taskStore;
        _conversationStore = conversationStore;
        _queryEngine = queryEngine;
        _pluginRuntime = pluginRuntime ?? new NullPluginRuntime();
    }

    public event Action<TaskRecord, TaskEvent>? TaskChanged;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var tasks = await _taskStore.ListAsync(cancellationToken);
        foreach (var task in tasks.Where(task => task.Status == ClawdTaskStatus.Running || task.Status == ClawdTaskStatus.Pending))
        {
            var interruptedAt = DateTimeOffset.UtcNow;
            var interruptedEvent = new TaskEvent(ClawdTaskStatus.Interrupted, "Task interrupted because ClawdNet restarted.", interruptedAt, true);
            var updated = task with
            {
                Status = ClawdTaskStatus.Interrupted,
                UpdatedAtUtc = interruptedAt,
                CompletedAtUtc = interruptedAt,
                LastStatusMessage = interruptedEvent.Message,
                Result = task.Result ?? new TaskResult(false, interruptedEvent.Message, interruptedEvent.Message),
                Events = TakeRecentEvents([.. task.Events ?? [], interruptedEvent]),
                InterruptionReason = interruptedEvent.Message
            };
            await _taskStore.SaveAsync(updated, cancellationToken);
        }
    }

    public async Task<TaskRecord> StartAsync(TaskRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var parentSession = await _conversationStore.GetAsync(request.ParentSessionId, cancellationToken);
        TaskRecord? parentTask = null;
        if (!string.IsNullOrWhiteSpace(request.ParentTaskId))
        {
            parentTask = await _taskStore.GetAsync(request.ParentTaskId, cancellationToken);
            if (parentTask is null)
            {
                throw new InvalidOperationException($"Parent task '{request.ParentTaskId}' was not found.");
            }

            if (!string.Equals(parentTask.ParentSessionId, request.ParentSessionId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Child tasks must use the same parent conversation session as the parent task.");
            }

            if (parentTask.Depth >= MaxDelegationDepth)
            {
                throw new InvalidOperationException($"Delegated tasks may only be nested {MaxDelegationDepth + 1} level(s) deep in this orchestration slice.");
            }
        }

        var provider = string.IsNullOrWhiteSpace(request.Provider)
            ? parentSession?.Provider ?? "anthropic"
            : request.Provider!;
        var model = string.IsNullOrWhiteSpace(request.Model)
            ? parentSession?.Model ?? "claude-sonnet-4-5"
            : request.Model!;
        var workerSession = await _conversationStore.CreateAsync(request.Title, model, cancellationToken, provider);
        var timestamp = DateTimeOffset.UtcNow;
        var startedEvent = new TaskEvent(ClawdTaskStatus.Running, parentTask is null ? "Task started." : "Child task started.", timestamp);
        var task = new TaskRecord(
            Guid.NewGuid().ToString("N"),
            TaskKind.Worker,
            string.IsNullOrWhiteSpace(request.Title) ? "Background task" : request.Title.Trim(),
            request.Goal.Trim(),
            request.ParentSessionId,
            workerSession.Id,
            model,
            request.PermissionMode,
            ClawdTaskStatus.Running,
            timestamp,
            timestamp,
            null,
            parentTask?.Id,
            parentTask?.RootTaskId ?? parentTask?.Id,
            parentTask is null ? 0 : parentTask.Depth + 1,
            request.ParentSummary,
            request.WorkingDirectory,
            startedEvent.Message,
            null,
            [startedEvent],
            [],
            "system: Worker session created.",
            workerSession.Messages.Count,
            workerSession.UpdatedAtUtc,
            null,
            provider);

        await _taskStore.CreateAsync(task, cancellationToken);
        if (parentTask is not null)
        {
            await LinkChildTaskAsync(parentTask, task, cancellationToken);
        }

        await AppendParentMessageAsync(task.ParentSessionId, "task_started", task.Id, $"{task.Title}: {startedEvent.Message}", false, cancellationToken);
        TaskChanged?.Invoke(task, startedEvent);

        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var execution = Task.Run(() => RunWorkerAsync(task, request, cts.Token), CancellationToken.None);
        _running[task.Id] = new RunningTaskHandle(cts, execution);
        return await _taskStore.GetAsync(task.Id, cancellationToken) ?? task;
    }

    public Task<TaskRecord?> GetAsync(string taskId, CancellationToken cancellationToken)
    {
        return _taskStore.GetAsync(taskId, cancellationToken);
    }

    public async Task<TaskRecord?> GetByWorkerSessionIdAsync(string workerSessionId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var tasks = await _taskStore.ListAsync(cancellationToken);
        return tasks.FirstOrDefault(task => string.Equals(task.WorkerSessionId, workerSessionId, StringComparison.Ordinal));
    }

    public async Task<IReadOnlyList<TaskEvent>> GetEventsAsync(string taskId, int limit, CancellationToken cancellationToken)
    {
        var task = await _taskStore.GetAsync(taskId, cancellationToken);
        if (task is null)
        {
            return [];
        }

        return (task.Events ?? [])
            .TakeLast(Math.Max(1, limit))
            .ToArray();
    }

    public async Task<TaskInspection?> InspectAsync(string taskId, CancellationToken cancellationToken)
    {
        var task = await _taskStore.GetAsync(taskId, cancellationToken);
        if (task is null)
        {
            return null;
        }

        var worker = await BuildWorkerSnapshotAsync(task, cancellationToken);
        var children = await GetChildTasksAsync(task.Id, cancellationToken);
        return new TaskInspection(task, (task.Events ?? []).TakeLast(TaskEventLimit).ToArray(), worker, children);
    }

    public Task<IReadOnlyList<TaskRecord>> ListAsync(CancellationToken cancellationToken)
    {
        return _taskStore.ListAsync(cancellationToken);
    }

    public async Task<TaskRecord?> CancelAsync(string taskId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var childTasks = await GetChildTasksAsync(taskId, cancellationToken);
        foreach (var childTask in childTasks.Where(child => child.Status == ClawdTaskStatus.Running || child.Status == ClawdTaskStatus.Pending))
        {
            await CancelAsync(childTask.Id, cancellationToken);
        }

        if (_running.TryGetValue(taskId, out var handle))
        {
            handle.Cancellation.Cancel();
            try
            {
                await handle.Execution;
            }
            catch
            {
                // Final status is persisted by the worker execution path.
            }
        }

        return await _taskStore.GetAsync(taskId, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var handle in _running.Values)
        {
            handle.Cancellation.Cancel();
        }

        foreach (var handle in _running.Values)
        {
            try
            {
                await handle.Execution;
            }
            catch
            {
                // ignore during shutdown
            }
            finally
            {
                handle.Cancellation.Dispose();
            }
        }
    }

    private async Task RunWorkerAsync(TaskRecord task, TaskRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var prompt = BuildWorkerPrompt(task, request);
            task = await RecordProgressAsync(task, "Worker launched.", cancellationToken);

            QueryExecutionResult? result = null;
            await foreach (var streamEvent in _queryEngine.StreamAskAsync(
                               new QueryRequest(
                                   prompt,
                                   task.WorkerSessionId,
                                   task.Model,
                                   request.MaxTurns,
                                   request.PermissionMode,
                                   null,
                                   task.Depth < MaxDelegationDepth,
                                   task.Provider),
                               cancellationToken))
            {
                switch (streamEvent)
                {
                    case UserTurnAcceptedEvent accepted:
                        task = await UpdateSnapshotAsync(task, accepted.Session, "Worker session created.", cancellationToken);
                        break;
                    case AssistantMessageCommittedEvent committed:
                        task = await UpdateSnapshotAsync(task, committed.Session, "Worker session updated.", cancellationToken);
                        break;
                    case ToolResultCommittedEvent committedTool:
                        task = await UpdateSnapshotAsync(task, committedTool.Session, $"Worker used tool {committedTool.ToolCall.Name}.", cancellationToken);
                        break;
                    case TurnCompletedStreamEvent completed:
                        result = completed.Result;
                        task = await UpdateSnapshotAsync(task, completed.Result.Session, "Task summary updated.", cancellationToken);
                        break;
                    case TurnFailedStreamEvent failed:
                        throw new InvalidOperationException(failed.Message);
                }
            }

            if (result is null)
            {
                throw new InvalidOperationException("Worker task completed without a final result.");
            }

            task = await _taskStore.GetAsync(task.Id, cancellationToken) ?? task;
            var childOutcome = await WaitForChildTasksAsync(task, cancellationToken);
            var completedAt = DateTimeOffset.UtcNow;
            var summary = Summarize(result.AssistantText);
            if (childOutcome.CompletedChildren.Count > 0)
            {
                summary = Summarize($"{summary} | child tasks: {childOutcome.Summary}");
            }

            if (childOutcome.HasFailures)
            {
                var failedSummary = Summarize($"Child task failure: {childOutcome.Summary}");
                var failedEvent = new TaskEvent(ClawdTaskStatus.Failed, failedSummary, completedAt, true);
                var storedFailed = await _taskStore.GetAsync(task.Id, cancellationToken);
                var failed = (storedFailed ?? task) with
                {
                    Status = ClawdTaskStatus.Failed,
                    UpdatedAtUtc = completedAt,
                    CompletedAtUtc = completedAt,
                    LastStatusMessage = failedSummary,
                    Result = new TaskResult(false, failedSummary, childOutcome.Summary),
                    Events = TakeRecentEvents([.. (storedFailed ?? task).Events ?? [], failedEvent])
                };
                await _taskStore.SaveAsync(failed, cancellationToken);
                await AppendParentMessageAsync(task.ParentSessionId, "task_failed", task.Id, failedSummary, true, cancellationToken);
                TaskChanged?.Invoke(failed, failedEvent);
                await RecordChildLifecycleAsync(failed, failedEvent, cancellationToken);
                await InvokeCompletionHooksAsync(failed, failed.Result, cancellationToken);
                return;
            }

            var taskResult = new TaskResult(true, summary);
            var completedEvent = new TaskEvent(ClawdTaskStatus.Completed, "Task completed successfully.", completedAt);
            var storedCompleted = await _taskStore.GetAsync(task.Id, cancellationToken);
            var updated = (storedCompleted ?? task) with
            {
                Status = ClawdTaskStatus.Completed,
                UpdatedAtUtc = completedAt,
                CompletedAtUtc = completedAt,
                LastStatusMessage = completedEvent.Message,
                Result = taskResult,
                Events = TakeRecentEvents([.. (storedCompleted ?? task).Events ?? [], completedEvent]),
                WorkerTranscriptTail = BuildTranscriptTail(result.Session),
                WorkerMessageCount = result.Session.Messages.Count,
                WorkerUpdatedAtUtc = result.Session.UpdatedAtUtc
            };
            await _taskStore.SaveAsync(updated, cancellationToken);
            await AppendParentMessageAsync(task.ParentSessionId, "task_completed", task.Id, $"{summary}", false, cancellationToken);
            TaskChanged?.Invoke(updated, completedEvent);
            await RecordChildLifecycleAsync(updated, completedEvent, cancellationToken);
            await InvokeCompletionHooksAsync(updated, taskResult, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            var canceledAt = DateTimeOffset.UtcNow;
            var canceledEvent = new TaskEvent(ClawdTaskStatus.Canceled, "Task canceled.", canceledAt, true);
            var storedCanceled = await _taskStore.GetAsync(task.Id, CancellationToken.None);
            var updated = (storedCanceled ?? task) with
            {
                Status = ClawdTaskStatus.Canceled,
                UpdatedAtUtc = canceledAt,
                CompletedAtUtc = canceledAt,
                LastStatusMessage = canceledEvent.Message,
                Result = new TaskResult(false, canceledEvent.Message, canceledEvent.Message),
                Events = TakeRecentEvents([.. (storedCanceled ?? task).Events ?? [], canceledEvent]),
                InterruptionReason = canceledEvent.Message
            };
            await _taskStore.SaveAsync(updated, CancellationToken.None);
            await AppendParentMessageAsync(task.ParentSessionId, "task_canceled", task.Id, canceledEvent.Message, true, CancellationToken.None);
            TaskChanged?.Invoke(updated, canceledEvent);
            await RecordChildLifecycleAsync(updated, canceledEvent, CancellationToken.None);
            await InvokeCompletionHooksAsync(updated, updated.Result, CancellationToken.None);
        }
        catch (Exception ex)
        {
            var failedAt = DateTimeOffset.UtcNow;
            var summary = Summarize(ex.Message);
            var failedEvent = new TaskEvent(ClawdTaskStatus.Failed, summary, failedAt, true);
            var storedFailed = await _taskStore.GetAsync(task.Id, CancellationToken.None);
            var updated = (storedFailed ?? task) with
            {
                Status = ClawdTaskStatus.Failed,
                UpdatedAtUtc = failedAt,
                CompletedAtUtc = failedAt,
                LastStatusMessage = summary,
                Result = new TaskResult(false, summary, ex.Message),
                Events = TakeRecentEvents([.. (storedFailed ?? task).Events ?? [], failedEvent]),
                InterruptionReason = ex.Message
            };
            await _taskStore.SaveAsync(updated, CancellationToken.None);
            await AppendParentMessageAsync(task.ParentSessionId, "task_failed", task.Id, summary, true, CancellationToken.None);
            TaskChanged?.Invoke(updated, failedEvent);
            await RecordChildLifecycleAsync(updated, failedEvent, CancellationToken.None);
            await InvokeCompletionHooksAsync(updated, updated.Result, CancellationToken.None);
        }
        finally
        {
            if (_running.TryRemove(task.Id, out var handle))
            {
                handle.Cancellation.Dispose();
            }
        }
    }

    private async Task AppendParentMessageAsync(
        string parentSessionId,
        string role,
        string taskId,
        string content,
        bool isError,
        CancellationToken cancellationToken)
    {
        var session = await _conversationStore.GetAsync(parentSessionId, cancellationToken);
        if (session is null)
        {
            return;
        }

        var timestamp = DateTimeOffset.UtcNow;
        var updated = session with
        {
            UpdatedAtUtc = timestamp,
            Messages =
            [
                .. session.Messages,
                new ConversationMessage(role, content, timestamp, taskId, taskId, isError)
            ]
        };
        await _conversationStore.SaveAsync(updated, cancellationToken);
    }

    private async Task InvokeCompletionHooksAsync(
        TaskRecord task,
        TaskResult? result,
        CancellationToken cancellationToken)
    {
        var hookResults = await _pluginRuntime.InvokeHooksAsync(
            new PluginHookInvocation(
                PluginHookKind.AfterTaskCompletion,
                task.ParentSessionId,
                task.Id,
                task.WorkingDirectory,
                new
                {
                    taskId = task.Id,
                    title = task.Title,
                    status = task.Status.ToString(),
                    summary = result?.Summary,
                    error = result?.Error,
                    workerSessionId = task.WorkerSessionId
                }),
            cancellationToken);

        foreach (var hookResult in hookResults)
        {
            await AppendParentMessageAsync(
                task.ParentSessionId,
                hookResult.Success ? "plugin_hook" : "plugin_hook_error",
                $"{hookResult.Plugin.Name}:{hookResult.Hook.Kind}",
                hookResult.Message,
                !hookResult.Success,
                cancellationToken);

            if (!hookResult.Success && hookResult.Blocking && task.Status == ClawdTaskStatus.Completed)
            {
                var failedAt = DateTimeOffset.UtcNow;
                var failureMessage = Summarize($"Plugin hook failed: {hookResult.Plugin.Name}/{hookResult.Hook.Kind} - {hookResult.Message}", MaxHookSummaryLength);
                var failureEvent = new TaskEvent(ClawdTaskStatus.Failed, failureMessage, failedAt, true);
                var failedTask = task with
                {
                    Status = ClawdTaskStatus.Failed,
                    UpdatedAtUtc = failedAt,
                    CompletedAtUtc = failedAt,
                    LastStatusMessage = failureMessage,
                    Result = new TaskResult(false, failureMessage, hookResult.Message),
                    Events = TakeRecentEvents([.. task.Events ?? [], failureEvent]),
                    InterruptionReason = hookResult.Message
                };
                await _taskStore.SaveAsync(failedTask, cancellationToken);
                await AppendParentMessageAsync(task.ParentSessionId, "task_failed", task.Id, failureMessage, true, cancellationToken);
                TaskChanged?.Invoke(failedTask, failureEvent);
                task = failedTask;
            }
        }
    }

    private async Task<TaskRecord> RecordProgressAsync(TaskRecord task, string message, CancellationToken cancellationToken)
    {
        var stored = await _taskStore.GetAsync(task.Id, cancellationToken);
        var timestamp = DateTimeOffset.UtcNow;
        var progressEvent = new TaskEvent(ClawdTaskStatus.Running, message, timestamp);
        var baseline = stored ?? task;
        var updated = baseline with
        {
            Status = ClawdTaskStatus.Running,
            UpdatedAtUtc = timestamp,
            LastStatusMessage = message,
            Events = TakeRecentEvents([.. baseline.Events ?? [], progressEvent])
        };
        await _taskStore.SaveAsync(updated, cancellationToken);
        await AppendParentMessageAsync(task.ParentSessionId, "task_updated", task.Id, message, false, cancellationToken);
        TaskChanged?.Invoke(updated, progressEvent);
        return updated;
    }

    private async Task<TaskRecord> UpdateSnapshotAsync(
        TaskRecord task,
        ConversationSession workerSession,
        string message,
        CancellationToken cancellationToken)
    {
        var stored = await _taskStore.GetAsync(task.Id, cancellationToken);
        var timestamp = DateTimeOffset.UtcNow;
        var progressEvent = new TaskEvent(ClawdTaskStatus.Running, message, timestamp);
        var baseline = stored ?? task;
        var updated = baseline with
        {
            Status = ClawdTaskStatus.Running,
            UpdatedAtUtc = timestamp,
            LastStatusMessage = message,
            Events = TakeRecentEvents([.. baseline.Events ?? [], progressEvent]),
            WorkerTranscriptTail = BuildTranscriptTail(workerSession),
            WorkerMessageCount = workerSession.Messages.Count,
            WorkerUpdatedAtUtc = workerSession.UpdatedAtUtc
        };
        await _taskStore.SaveAsync(updated, cancellationToken);
        await AppendParentMessageAsync(task.ParentSessionId, "task_updated", task.Id, message, false, cancellationToken);
        TaskChanged?.Invoke(updated, progressEvent);
        return updated;
    }

    private async Task<TaskWorkerSnapshot> BuildWorkerSnapshotAsync(TaskRecord task, CancellationToken cancellationToken)
    {
        var workerSession = await _conversationStore.GetAsync(task.WorkerSessionId, cancellationToken);
        if (workerSession is null)
        {
            return new TaskWorkerSnapshot(
                task.WorkerSessionId,
                task.WorkerMessageCount,
                task.WorkerUpdatedAtUtc,
                task.WorkerTranscriptTail ?? "(worker session unavailable)");
        }

        return new TaskWorkerSnapshot(
            workerSession.Id,
            workerSession.Messages.Count,
            workerSession.UpdatedAtUtc,
            BuildTranscriptTail(workerSession));
    }

    private async Task<ChildTaskOutcome> WaitForChildTasksAsync(TaskRecord task, CancellationToken cancellationToken)
    {
        var notified = false;

        while (true)
        {
            var children = await GetChildTasksAsync(task.Id, cancellationToken);
            var activeChildren = children
                .Where(child => child.Status == ClawdTaskStatus.Running || child.Status == ClawdTaskStatus.Pending)
                .ToArray();
            if (activeChildren.Length == 0)
            {
                var failedChildren = children
                    .Where(child => child.Status is ClawdTaskStatus.Failed or ClawdTaskStatus.Canceled or ClawdTaskStatus.Interrupted)
                    .ToArray();
                return new ChildTaskOutcome(
                    children,
                    failedChildren,
                    failedChildren.Length > 0,
                    children.Count == 0
                        ? "none"
                        : string.Join(", ", children.Select(child => $"{child.Id}:{child.Status}")));
            }

            if (!notified)
            {
                task = await RecordProgressAsync(task, $"Waiting for {activeChildren.Length} child task(s).", cancellationToken);
                notified = true;
            }

            await Task.Delay(50, cancellationToken);
        }
    }

    private async Task<IReadOnlyList<TaskRecord>> GetChildTasksAsync(string parentTaskId, CancellationToken cancellationToken)
    {
        var tasks = await _taskStore.ListAsync(cancellationToken);
        return tasks
            .Where(task => string.Equals(task.ParentTaskId, parentTaskId, StringComparison.Ordinal))
            .OrderByDescending(task => task.UpdatedAtUtc)
            .ToArray();
    }

    private async Task LinkChildTaskAsync(TaskRecord parentTask, TaskRecord childTask, CancellationToken cancellationToken)
    {
        var linked = parentTask with
        {
            UpdatedAtUtc = childTask.CreatedAtUtc,
            LastStatusMessage = $"Delegated child task started: {childTask.Title}",
            ChildTaskIds = [.. parentTask.ChildTaskIds ?? [], childTask.Id]
        };
        await _taskStore.SaveAsync(linked, cancellationToken);
        var childEvent = new TaskEvent(ClawdTaskStatus.Running, $"Delegated child task started: {childTask.Title} ({childTask.Id})", childTask.CreatedAtUtc);
        await _taskStore.AppendEventAsync(parentTask.Id, childEvent, cancellationToken);
        TaskChanged?.Invoke(await _taskStore.GetAsync(parentTask.Id, cancellationToken) ?? linked, childEvent);
    }

    private async Task RecordChildLifecycleAsync(TaskRecord task, TaskEvent lifecycleEvent, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(task.ParentTaskId))
        {
            return;
        }

        var parentTask = await _taskStore.GetAsync(task.ParentTaskId, cancellationToken);
        if (parentTask is null)
        {
            return;
        }

        var message = $"Child task {task.Id} | {task.Status} | {task.Title}";
        var parentEvent = new TaskEvent(
            ClawdTaskStatus.Running,
            message,
            lifecycleEvent.TimestampUtc,
            lifecycleEvent.IsError);
        await _taskStore.AppendEventAsync(parentTask.Id, parentEvent, cancellationToken);
        var updatedParent = await _taskStore.GetAsync(parentTask.Id, cancellationToken) ?? parentTask;
        await AppendParentMessageAsync(parentTask.ParentSessionId, "task_updated", parentTask.Id, message, lifecycleEvent.IsError, cancellationToken);
        TaskChanged?.Invoke(updatedParent, parentEvent);
    }

    private static string BuildWorkerPrompt(TaskRecord task, TaskRequest request)
    {
        var lines = new List<string>
        {
            $"Task: {request.Title}",
            $"Goal: {request.Goal}"
        };

        if (!string.IsNullOrWhiteSpace(request.ParentSummary))
        {
            lines.Add($"Parent context: {request.ParentSummary}");
        }

        if (!string.IsNullOrWhiteSpace(request.WorkingDirectory))
        {
            lines.Add($"Working directory: {request.WorkingDirectory}");
        }

        if (task.Depth < MaxDelegationDepth)
        {
            lines.Add("You may delegate by calling task_start if a tightly scoped child task would help.");
        }

        if (!string.IsNullOrWhiteSpace(request.ParentTaskId))
        {
            lines.Add($"Parent task id: {request.ParentTaskId}");
            lines.Add("If you must delegate, create at most one additional child task and keep it tightly scoped.");
        }

        lines.Add("Complete this task independently and return a concise completion summary.");
        return string.Join(Environment.NewLine, lines);
    }

    private static string Summarize(string text, int maxLength = 120)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "No summary available.";
        }

        var normalized = text.Replace(Environment.NewLine, " ", StringComparison.Ordinal).Trim();
        return normalized.Length <= maxLength
            ? normalized
            : $"{normalized[..(maxLength - 3)]}...";
    }

    private static string Summarize(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length <= 240)
        {
            return trimmed;
        }

        return $"{trimmed[..237]}...";
    }

    private static IReadOnlyList<TaskEvent> TakeRecentEvents(IEnumerable<TaskEvent> events)
    {
        return events.TakeLast(TaskEventLimit).ToArray();
    }

    private static string BuildTranscriptTail(ConversationSession session)
    {
        var tail = string.Join(
            Environment.NewLine,
            session.Messages
                .TakeLast(8)
                .Select(message => $"{message.Role}: {message.Content}"));

        if (tail.Length <= WorkerTranscriptTailLength)
        {
            return tail;
        }

        return tail[^WorkerTranscriptTailLength..];
    }

    private sealed record RunningTaskHandle(
        CancellationTokenSource Cancellation,
        Task Execution);

    private sealed record ChildTaskOutcome(
        IReadOnlyList<TaskRecord> CompletedChildren,
        IReadOnlyList<TaskRecord> FailedChildren,
        bool HasFailures,
        string Summary);
}
