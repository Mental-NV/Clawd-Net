using System.Collections.Concurrent;
using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Models;
using ClawdNet.Runtime.Plugins;
using ClawdTaskStatus = ClawdNet.Core.Models.TaskStatus;

namespace ClawdNet.Runtime.Tasks;

public sealed class TaskManager : ITaskManager
{
    private const int MaxHookSummaryLength = 240;
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
                Events = [.. task.Events ?? [], interruptedEvent]
            };
            await _taskStore.SaveAsync(updated, cancellationToken);
        }
    }

    public async Task<TaskRecord> StartAsync(TaskRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var model = string.IsNullOrWhiteSpace(request.Model) ? "claude-sonnet-4-5" : request.Model!;
        var workerSession = await _conversationStore.CreateAsync(request.Title, model, cancellationToken);
        var timestamp = DateTimeOffset.UtcNow;
        var startedEvent = new TaskEvent(ClawdTaskStatus.Running, "Task started.", timestamp);
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
            request.ParentSummary,
            request.WorkingDirectory,
            startedEvent.Message,
            null,
            [startedEvent]);

        await _taskStore.CreateAsync(task, cancellationToken);
        await AppendParentMessageAsync(task.ParentSessionId, "task_started", task.Id, $"{task.Title}: {startedEvent.Message}", false, cancellationToken);
        TaskChanged?.Invoke(task, startedEvent);

        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var execution = Task.Run(() => RunWorkerAsync(task, request, cts.Token), CancellationToken.None);
        _running[task.Id] = new RunningTaskHandle(cts, execution);
        return task;
    }

    public Task<TaskRecord?> GetAsync(string taskId, CancellationToken cancellationToken)
    {
        return _taskStore.GetAsync(taskId, cancellationToken);
    }

    public Task<IReadOnlyList<TaskRecord>> ListAsync(CancellationToken cancellationToken)
    {
        return _taskStore.ListAsync(cancellationToken);
    }

    public async Task<TaskRecord?> CancelAsync(string taskId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
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
            var prompt = BuildWorkerPrompt(request);
            var result = await _queryEngine.AskAsync(
                new QueryRequest(
                    prompt,
                    task.WorkerSessionId,
                    task.Model,
                    request.MaxTurns,
                    request.PermissionMode,
                    null,
                    false),
                cancellationToken);

            var completedAt = DateTimeOffset.UtcNow;
            var summary = Summarize(result.AssistantText);
            var taskResult = new TaskResult(true, summary);
            var completedEvent = new TaskEvent(ClawdTaskStatus.Completed, "Task completed successfully.", completedAt);
            var updated = task with
            {
                Status = ClawdTaskStatus.Completed,
                UpdatedAtUtc = completedAt,
                CompletedAtUtc = completedAt,
                LastStatusMessage = completedEvent.Message,
                Result = taskResult,
                Events = [.. task.Events ?? [], completedEvent]
            };
            await _taskStore.SaveAsync(updated, cancellationToken);
            await AppendParentMessageAsync(task.ParentSessionId, "task_completed", task.Id, $"{summary}", false, cancellationToken);
            TaskChanged?.Invoke(updated, completedEvent);
            await InvokeCompletionHooksAsync(updated, taskResult, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            var canceledAt = DateTimeOffset.UtcNow;
            var canceledEvent = new TaskEvent(ClawdTaskStatus.Canceled, "Task canceled.", canceledAt, true);
            var updated = task with
            {
                Status = ClawdTaskStatus.Canceled,
                UpdatedAtUtc = canceledAt,
                CompletedAtUtc = canceledAt,
                LastStatusMessage = canceledEvent.Message,
                Result = new TaskResult(false, canceledEvent.Message, canceledEvent.Message),
                Events = [.. task.Events ?? [], canceledEvent]
            };
            await _taskStore.SaveAsync(updated, CancellationToken.None);
            await AppendParentMessageAsync(task.ParentSessionId, "task_canceled", task.Id, canceledEvent.Message, true, CancellationToken.None);
            TaskChanged?.Invoke(updated, canceledEvent);
            await InvokeCompletionHooksAsync(updated, updated.Result, CancellationToken.None);
        }
        catch (Exception ex)
        {
            var failedAt = DateTimeOffset.UtcNow;
            var summary = Summarize(ex.Message);
            var failedEvent = new TaskEvent(ClawdTaskStatus.Failed, summary, failedAt, true);
            var updated = task with
            {
                Status = ClawdTaskStatus.Failed,
                UpdatedAtUtc = failedAt,
                CompletedAtUtc = failedAt,
                LastStatusMessage = summary,
                Result = new TaskResult(false, summary, ex.Message),
                Events = [.. task.Events ?? [], failedEvent]
            };
            await _taskStore.SaveAsync(updated, CancellationToken.None);
            await AppendParentMessageAsync(task.ParentSessionId, "task_failed", task.Id, summary, true, CancellationToken.None);
            TaskChanged?.Invoke(updated, failedEvent);
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
                    Events = [.. task.Events ?? [], failureEvent]
                };
                await _taskStore.SaveAsync(failedTask, cancellationToken);
                await AppendParentMessageAsync(task.ParentSessionId, "task_failed", task.Id, failureMessage, true, cancellationToken);
                TaskChanged?.Invoke(failedTask, failureEvent);
                task = failedTask;
            }
        }
    }

    private static string BuildWorkerPrompt(TaskRequest request)
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

    private sealed record RunningTaskHandle(
        CancellationTokenSource Cancellation,
        Task Execution);
}
