using ClawdNet.Core.Models;

namespace ClawdNet.Core.Abstractions;

public interface ITaskManager : IAsyncDisposable
{
    event Action<TaskRecord, TaskEvent>? TaskChanged;

    Task InitializeAsync(CancellationToken cancellationToken);

    Task<TaskRecord> StartAsync(TaskRequest request, CancellationToken cancellationToken);

    Task<TaskRecord?> GetAsync(string taskId, CancellationToken cancellationToken);

    Task<TaskRecord?> GetByWorkerSessionIdAsync(string workerSessionId, CancellationToken cancellationToken);

    Task<IReadOnlyList<TaskEvent>> GetEventsAsync(string taskId, int limit, CancellationToken cancellationToken);

    Task<TaskInspection?> InspectAsync(string taskId, CancellationToken cancellationToken);

    Task<IReadOnlyList<TaskRecord>> ListAsync(CancellationToken cancellationToken);

    Task<TaskRecord?> CancelAsync(string taskId, CancellationToken cancellationToken);
}
