using System.Text.Json;
using System.Text.Json.Nodes;
using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Models;

namespace ClawdNet.Runtime.Tools;

public sealed class TaskListTool : ITool
{
    private readonly ITaskManager _taskManager;

    public TaskListTool(ITaskManager taskManager)
    {
        _taskManager = taskManager;
    }

    public string Name => "task_list";

    public string Description => "List recent worker tasks.";

    public ToolCategory Category => ToolCategory.ReadOnly;

    public JsonObject InputSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["limit"] = new JsonObject { ["type"] = "integer" }
        }
    };

    public async Task<ToolExecutionResult> ExecuteAsync(ToolExecutionRequest request, CancellationToken cancellationToken)
    {
        var limit = request.Input?["limit"]?.GetValue<int?>() ?? 10;
        var tasks = await _taskManager.ListAsync(cancellationToken);
        var payload = tasks
            .Take(Math.Max(1, limit))
            .Select(task => new
            {
                taskId = task.Id,
                parentTaskId = task.ParentTaskId,
                rootTaskId = task.RootTaskId,
                depth = task.Depth,
                provider = task.Provider,
                status = task.Status.ToString(),
                title = task.Title,
                updatedAtUtc = task.UpdatedAtUtc,
                workerSessionId = task.WorkerSessionId,
                summary = task.Result?.Summary ?? task.LastStatusMessage,
                progressPercent = task.ProgressPercent,
                progressMessage = task.ProgressMessage,
                dependsOnTaskIds = task.DependsOnTaskIds ?? [],
                dependencyCount = task.DependsOnTaskIds?.Count ?? 0,
                childTaskCount = task.ChildTaskIds?.Count ?? 0,
                workerMessageCount = task.WorkerMessageCount,
                workerUpdatedAtUtc = task.WorkerUpdatedAtUtc
            });
        return new ToolExecutionResult(true, JsonSerializer.Serialize(payload));
    }
}
