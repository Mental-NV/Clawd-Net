using ClawdNet.Core.Models;
using ClawdNet.Runtime.Sessions;
using ClawdNet.Runtime.Tasks;
using ClawdNet.Tests.TestDoubles;
using ClawdTaskStatus = ClawdNet.Core.Models.TaskStatus;

namespace ClawdNet.Tests;

public sealed class TaskManagerTests : IDisposable
{
    private readonly string _dataRoot = Path.Combine(Path.GetTempPath(), "clawdnet-task-manager-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Task_manager_starts_worker_task_and_persists_completion()
    {
        var sessionStore = new JsonSessionStore(_dataRoot);
        var taskStore = new JsonTaskStore(_dataRoot);
        var engine = new FakeQueryEngine
        {
            Handler = async request =>
            {
                var worker = await sessionStore.GetAsync(request.SessionId!, CancellationToken.None)
                    ?? throw new InvalidOperationException("Expected worker session.");
                var updated = worker with
                {
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                    Messages =
                    [
                        .. worker.Messages,
                        new ConversationMessage("assistant", "worker finished", DateTimeOffset.UtcNow)
                    ]
                };
                await sessionStore.SaveAsync(updated, CancellationToken.None);
                return new QueryExecutionResult(updated, "worker finished", 1);
            }
        };
        var manager = new TaskManager(taskStore, sessionStore, engine);
        await manager.InitializeAsync(CancellationToken.None);
        var parent = await sessionStore.CreateAsync("Parent", "claude-sonnet-4-5", CancellationToken.None);

        var task = await manager.StartAsync(new TaskRequest("Index repo", "Scan files", parent.Id), CancellationToken.None);
        var completed = await WaitForTaskAsync(taskStore, task.Id, ClawdTaskStatus.Completed);
        var parentSession = await sessionStore.GetAsync(parent.Id, CancellationToken.None);

        Assert.NotNull(completed);
        Assert.Equal(ClawdTaskStatus.Completed, completed!.Status);
        Assert.NotNull(completed.Result);
        Assert.NotNull(parentSession);
        Assert.Contains(parentSession!.Messages, message => message.Role == "task_started");
        Assert.Contains(parentSession.Messages, message => message.Role == "task_completed");
    }

    [Fact]
    public async Task Task_manager_initialization_marks_running_tasks_interrupted()
    {
        var sessionStore = new JsonSessionStore(_dataRoot);
        var taskStore = new JsonTaskStore(_dataRoot);
        var timestamp = DateTimeOffset.UtcNow;
        await taskStore.CreateAsync(
            new TaskRecord(
                "task-1",
                TaskKind.Worker,
                "Stale task",
                "Run forever",
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
                [new TaskEvent(ClawdTaskStatus.Running, "Task started.", timestamp)]),
            CancellationToken.None);
        var manager = new TaskManager(taskStore, sessionStore, new FakeQueryEngine());

        await manager.InitializeAsync(CancellationToken.None);
        var reloaded = await taskStore.GetAsync("task-1", CancellationToken.None);

        Assert.NotNull(reloaded);
        Assert.Equal(ClawdTaskStatus.Interrupted, reloaded!.Status);
    }

    [Fact]
    public async Task Task_manager_cancel_updates_final_status()
    {
        var sessionStore = new JsonSessionStore(_dataRoot);
        var taskStore = new JsonTaskStore(_dataRoot);
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var engine = new FakeQueryEngine
        {
            HandlerWithCancellation = async (_, cancellationToken) =>
            {
                await gate.Task.WaitAsync(cancellationToken);
                return new QueryExecutionResult(
                    new ConversationSession("worker", "Worker", "claude-sonnet-4-5", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, []),
                    "done",
                    1);
            }
        };
        var manager = new TaskManager(taskStore, sessionStore, engine);
        await manager.InitializeAsync(CancellationToken.None);
        var parent = await sessionStore.CreateAsync("Parent", "claude-sonnet-4-5", CancellationToken.None);
        var task = await manager.StartAsync(new TaskRequest("Cancelable", "Wait", parent.Id), CancellationToken.None);

        var canceled = await manager.CancelAsync(task.Id, CancellationToken.None);
        gate.TrySetCanceled();

        Assert.NotNull(canceled);
        Assert.Equal(ClawdTaskStatus.Canceled, canceled!.Status);
    }

    private static async Task<TaskRecord?> WaitForTaskAsync(JsonTaskStore store, string taskId, ClawdTaskStatus expectedStatus)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var task = await store.GetAsync(taskId, CancellationToken.None);
            if (task?.Status == expectedStatus)
            {
                return task;
            }

            await Task.Delay(20);
        }

        return await store.GetAsync(taskId, CancellationToken.None);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataRoot))
        {
            Directory.Delete(_dataRoot, recursive: true);
        }
    }
}
