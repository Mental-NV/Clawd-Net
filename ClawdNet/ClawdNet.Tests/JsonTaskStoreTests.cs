using ClawdNet.Core.Models;
using ClawdNet.Runtime.Sessions;
using ClawdTaskStatus = ClawdNet.Core.Models.TaskStatus;

namespace ClawdNet.Tests;

public sealed class JsonTaskStoreTests : IDisposable
{
    private readonly string _dataRoot = Path.Combine(Path.GetTempPath(), "clawdnet-task-store-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Task_store_creates_lists_and_appends_events()
    {
        var store = new JsonTaskStore(_dataRoot);
        var timestamp = DateTimeOffset.UtcNow;
        var record = new TaskRecord(
            "task-1",
            TaskKind.Worker,
            "Index repo",
            "Scan the repository",
            "parent-1",
            "worker-1",
            "claude-sonnet-4-5",
            PermissionMode.Default,
            ClawdTaskStatus.Running,
            timestamp,
            timestamp,
            null,
            null,
            null,
            "Task started.",
            null,
            [new TaskEvent(ClawdTaskStatus.Running, "Task started.", timestamp)]);

        await store.CreateAsync(record, CancellationToken.None);
        await store.AppendEventAsync("task-1", new TaskEvent(ClawdTaskStatus.Completed, "Task completed.", timestamp.AddSeconds(1)), CancellationToken.None);
        var reloaded = await store.GetAsync("task-1", CancellationToken.None);
        var listed = await store.ListAsync(CancellationToken.None);

        Assert.NotNull(reloaded);
        Assert.Equal(ClawdTaskStatus.Completed, reloaded!.Status);
        Assert.Equal(2, reloaded.Events?.Count);
        Assert.Single(listed);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataRoot))
        {
            Directory.Delete(_dataRoot, recursive: true);
        }
    }
}
