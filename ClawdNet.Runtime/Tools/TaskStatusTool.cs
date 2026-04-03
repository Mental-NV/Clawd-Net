using System.Text.Json;
using System.Text.Json.Nodes;
using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Models;

namespace ClawdNet.Runtime.Tools;

public sealed class TaskStatusTool : ITool
{
    private readonly ITaskManager _taskManager;

    public TaskStatusTool(ITaskManager taskManager)
    {
        _taskManager = taskManager;
    }

    public string Name => "task_status";

    public string Description => "Read one persisted task status and summary.";

    public ToolCategory Category => ToolCategory.ReadOnly;

    public JsonObject InputSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["taskId"] = new JsonObject { ["type"] = "string" }
        },
        ["required"] = new JsonArray("taskId")
    };

    public async Task<ToolExecutionResult> ExecuteAsync(ToolExecutionRequest request, CancellationToken cancellationToken)
    {
        var taskId = request.Input?["taskId"]?.GetValue<string>()?.Trim();
        if (string.IsNullOrWhiteSpace(taskId))
        {
            return new ToolExecutionResult(false, string.Empty, "task_status requires a 'taskId' string.");
        }

        var task = await _taskManager.GetAsync(taskId, cancellationToken);
        if (task is null)
        {
            return new ToolExecutionResult(false, string.Empty, $"Task '{taskId}' was not found.");
        }

        return new ToolExecutionResult(true, JsonSerializer.Serialize(new
        {
            taskId = task.Id,
            parentTaskId = task.ParentTaskId,
            rootTaskId = task.RootTaskId,
            depth = task.Depth,
            provider = task.Provider,
            status = task.Status.ToString(),
            title = task.Title,
            workerSessionId = task.WorkerSessionId,
            updatedAtUtc = task.UpdatedAtUtc,
            lastStatusMessage = task.LastStatusMessage,
            progressPercent = task.ProgressPercent,
            progressMessage = task.ProgressMessage,
            result = task.Result,
            childTaskIds = task.ChildTaskIds ?? [],
            childTaskCount = task.ChildTaskIds?.Count ?? 0,
            workerMessageCount = task.WorkerMessageCount,
            workerUpdatedAtUtc = task.WorkerUpdatedAtUtc,
            recentEvents = (task.Events ?? []).TakeLast(5).ToArray()
        }));
    }
}
