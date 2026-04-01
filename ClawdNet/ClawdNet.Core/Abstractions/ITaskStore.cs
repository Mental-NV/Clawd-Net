using ClawdNet.Core.Models;

namespace ClawdNet.Core.Abstractions;

public interface ITaskStore
{
    Task CreateAsync(TaskRecord task, CancellationToken cancellationToken);

    Task<TaskRecord?> GetAsync(string taskId, CancellationToken cancellationToken);

    Task<IReadOnlyList<TaskRecord>> ListAsync(CancellationToken cancellationToken);

    Task SaveAsync(TaskRecord task, CancellationToken cancellationToken);

    Task AppendEventAsync(string taskId, TaskEvent taskEvent, CancellationToken cancellationToken);
}
