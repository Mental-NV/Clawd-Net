using System.Text.Json;
using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Models;

namespace ClawdNet.Runtime.Sessions;

public sealed class JsonTaskStore : ITaskStore
{
    private const int MaxEvents = 20;
    private const int MaxTranscriptTailLength = 1200;
    private readonly string _storePath;
    private readonly SemaphoreSlim _sync = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public JsonTaskStore(string rootDirectory)
    {
        Directory.CreateDirectory(rootDirectory);
        _storePath = Path.Combine(rootDirectory, "tasks.json");
    }

    public async Task CreateAsync(TaskRecord task, CancellationToken cancellationToken)
    {
        await _sync.WaitAsync(cancellationToken);
        try
        {
            var tasks = await ReadTasksAsync(cancellationToken);
            tasks.Add(Normalize(task));
            await WriteTasksAsync(tasks, cancellationToken);
        }
        finally
        {
            _sync.Release();
        }
    }

    public async Task<TaskRecord?> GetAsync(string taskId, CancellationToken cancellationToken)
    {
        await _sync.WaitAsync(cancellationToken);
        try
        {
            var tasks = await ReadTasksAsync(cancellationToken);
            return tasks.FirstOrDefault(task => string.Equals(task.Id, taskId, StringComparison.Ordinal));
        }
        finally
        {
            _sync.Release();
        }
    }

    public async Task<IReadOnlyList<TaskRecord>> ListAsync(CancellationToken cancellationToken)
    {
        await _sync.WaitAsync(cancellationToken);
        try
        {
            var tasks = await ReadTasksAsync(cancellationToken);
            return tasks
                .OrderByDescending(task => task.UpdatedAtUtc)
                .ToArray();
        }
        finally
        {
            _sync.Release();
        }
    }

    public async Task SaveAsync(TaskRecord task, CancellationToken cancellationToken)
    {
        await _sync.WaitAsync(cancellationToken);
        try
        {
            var tasks = await ReadTasksAsync(cancellationToken);
            var index = tasks.FindIndex(existing => string.Equals(existing.Id, task.Id, StringComparison.Ordinal));
            if (index < 0)
            {
                throw new InvalidOperationException($"Task '{task.Id}' was not found.");
            }

            tasks[index] = Normalize(task);
            await WriteTasksAsync(tasks, cancellationToken);
        }
        finally
        {
            _sync.Release();
        }
    }

    public async Task AppendEventAsync(string taskId, TaskEvent taskEvent, CancellationToken cancellationToken)
    {
        await _sync.WaitAsync(cancellationToken);
        try
        {
            var tasks = await ReadTasksAsync(cancellationToken);
            var index = tasks.FindIndex(existing => string.Equals(existing.Id, taskId, StringComparison.Ordinal));
            if (index < 0)
            {
                throw new InvalidOperationException($"Task '{taskId}' was not found.");
            }

            var task = tasks[index];
            tasks[index] = Normalize(task with
            {
                UpdatedAtUtc = taskEvent.TimestampUtc,
                LastStatusMessage = taskEvent.Message,
                Status = taskEvent.Status,
                Events = [.. task.Events ?? [], taskEvent]
            });
            await WriteTasksAsync(tasks, cancellationToken);
        }
        finally
        {
            _sync.Release();
        }
    }

    private async Task<List<TaskRecord>> ReadTasksAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_storePath))
        {
            return [];
        }

        await using var stream = File.OpenRead(_storePath);
        if (stream.Length == 0)
        {
            return [];
        }

        var tasks = await JsonSerializer.DeserializeAsync<List<TaskRecord>>(stream, _jsonOptions, cancellationToken);
        return tasks?.Select(Normalize).ToList() ?? [];
    }

    private async Task WriteTasksAsync(List<TaskRecord> tasks, CancellationToken cancellationToken)
    {
        await using var stream = File.Create(_storePath);
        await JsonSerializer.SerializeAsync(stream, tasks, _jsonOptions, cancellationToken);
    }

    private static TaskRecord Normalize(TaskRecord task)
    {
        var createdAt = task.CreatedAtUtc == default ? DateTimeOffset.UtcNow : task.CreatedAtUtc;
        var updatedAt = task.UpdatedAtUtc == default ? createdAt : task.UpdatedAtUtc;
        var model = string.IsNullOrWhiteSpace(task.Model) ? "claude-sonnet-4-5" : task.Model;
        var provider = string.IsNullOrWhiteSpace(task.Provider) ? "anthropic" : task.Provider;
        return task with
        {
            CreatedAtUtc = createdAt,
            UpdatedAtUtc = updatedAt,
            Model = model,
            Provider = provider,
            RootTaskId = string.IsNullOrWhiteSpace(task.RootTaskId) ? task.Id : task.RootTaskId,
            Depth = Math.Max(0, task.Depth),
            Events = (task.Events ?? []).TakeLast(MaxEvents).ToArray(),
            ChildTaskIds = (task.ChildTaskIds ?? []).Distinct(StringComparer.Ordinal).ToArray(),
            WorkerTranscriptTail = NormalizeTranscript(task.WorkerTranscriptTail),
            WorkerUpdatedAtUtc = task.WorkerUpdatedAtUtc ?? updatedAt
        };
    }

    private static string? NormalizeTranscript(string? transcript)
    {
        if (string.IsNullOrWhiteSpace(transcript))
        {
            return transcript;
        }

        var trimmed = transcript.Trim();
        return trimmed.Length <= MaxTranscriptTailLength
            ? trimmed
            : trimmed[^MaxTranscriptTailLength..];
    }
}
