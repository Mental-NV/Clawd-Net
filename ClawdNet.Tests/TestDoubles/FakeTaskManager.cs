using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Models;
using ClawdTaskStatus = ClawdNet.Core.Models.TaskStatus;

namespace ClawdNet.Tests.TestDoubles;

public sealed class FakeTaskManager : ITaskManager
{
    private readonly List<TaskRecord> _tasks = [];

    public event Action<TaskRecord, TaskEvent>? TaskChanged;

    public List<TaskRequest> Starts { get; } = [];

    public List<string> Cancellations { get; } = [];

    public Func<TaskRequest, TaskRecord>? StartHandler { get; set; }

    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task<TaskRecord> StartAsync(TaskRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Starts.Add(request);
        var task = StartHandler?.Invoke(request) ?? NewTask(request, ClawdTaskStatus.Running, "Task started.");
        _tasks.RemoveAll(existing => string.Equals(existing.Id, task.Id, StringComparison.Ordinal));
        _tasks.Add(task);
        var taskEvent = task.Events?.LastOrDefault() ?? new TaskEvent(task.Status, task.LastStatusMessage ?? "Task started.", DateTimeOffset.UtcNow);
        TaskChanged?.Invoke(task, taskEvent);
        return Task.FromResult(task);
    }

    public Task<TaskRecord?> GetAsync(string taskId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_tasks.FirstOrDefault(task => string.Equals(task.Id, taskId, StringComparison.Ordinal)));
    }

    public Task<TaskRecord?> GetByWorkerSessionIdAsync(string workerSessionId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_tasks.FirstOrDefault(task => string.Equals(task.WorkerSessionId, workerSessionId, StringComparison.Ordinal)));
    }

    public async Task<IReadOnlyList<TaskEvent>> GetEventsAsync(string taskId, int limit, CancellationToken cancellationToken)
    {
        var task = await GetAsync(taskId, cancellationToken);
        return (task?.Events ?? []).TakeLast(Math.Max(1, limit)).ToArray();
    }

    public async Task<TaskInspection?> InspectAsync(string taskId, CancellationToken cancellationToken)
    {
        var task = await GetAsync(taskId, cancellationToken);
        if (task is null)
        {
            return null;
        }

        return new TaskInspection(
            task,
            (task.Events ?? []).TakeLast(8).ToArray(),
            new TaskWorkerSnapshot(
                task.WorkerSessionId,
                task.WorkerMessageCount,
                task.WorkerUpdatedAtUtc,
                task.WorkerTranscriptTail ?? "(no worker transcript)"),
            _tasks.Where(child => string.Equals(child.ParentTaskId, task.Id, StringComparison.Ordinal)).OrderByDescending(child => child.UpdatedAtUtc).ToArray());
    }

    public Task<IReadOnlyList<TaskRecord>> ListAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<TaskRecord>>(_tasks.OrderByDescending(task => task.UpdatedAtUtc).ToArray());
    }

    public Task<TaskRecord?> CancelAsync(string taskId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Cancellations.Add(taskId);
        var task = _tasks.FirstOrDefault(existing => string.Equals(existing.Id, taskId, StringComparison.Ordinal));
        if (task is null)
        {
            return Task.FromResult<TaskRecord?>(null);
        }

        var canceledEvent = new TaskEvent(ClawdTaskStatus.Canceled, "Task canceled.", DateTimeOffset.UtcNow, true);
        var updated = task with
        {
            Status = ClawdTaskStatus.Canceled,
            UpdatedAtUtc = canceledEvent.TimestampUtc,
            CompletedAtUtc = canceledEvent.TimestampUtc,
            LastStatusMessage = canceledEvent.Message,
            Result = new TaskResult(false, canceledEvent.Message, canceledEvent.Message),
            Events = [.. task.Events ?? [], canceledEvent]
        };
        _tasks.Remove(task);
        _tasks.Add(updated);
        TaskChanged?.Invoke(updated, canceledEvent);
        return Task.FromResult<TaskRecord?>(updated);
    }

    public void Publish(TaskRecord task, TaskEvent taskEvent)
    {
        _tasks.RemoveAll(existing => string.Equals(existing.Id, task.Id, StringComparison.Ordinal));
        _tasks.Add(task);
        TaskChanged?.Invoke(task, taskEvent);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public static TaskRecord NewTask(TaskRequest request, ClawdTaskStatus status, string message)
    {
        var timestamp = DateTimeOffset.UtcNow;
        return new TaskRecord(
            Guid.NewGuid().ToString("N"),
            TaskKind.Worker,
            request.Title,
            request.Goal,
            request.ParentSessionId,
            Guid.NewGuid().ToString("N"),
            request.Model ?? "claude-sonnet-4-5",
            request.PermissionMode,
            status,
            timestamp,
            timestamp,
            status is ClawdTaskStatus.Completed or ClawdTaskStatus.Canceled or ClawdTaskStatus.Failed or ClawdTaskStatus.Interrupted ? timestamp : null,
            request.ParentTaskId,
            request.ParentTaskId,
            string.IsNullOrWhiteSpace(request.ParentTaskId) ? 0 : 1,
            request.ParentSummary,
            request.WorkingDirectory,
            message,
            status == ClawdTaskStatus.Completed ? new TaskResult(true, message) : null,
            [new TaskEvent(status, message, timestamp, status != ClawdTaskStatus.Completed && status != ClawdTaskStatus.Running)],
            [],
            "assistant: worker snapshot",
            2,
            timestamp,
            status == ClawdTaskStatus.Interrupted ? message : null);
    }
}
