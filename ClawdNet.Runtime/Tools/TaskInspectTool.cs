using System.Text.Json;
using System.Text.Json.Nodes;
using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Models;

namespace ClawdNet.Runtime.Tools;

public sealed class TaskInspectTool : ITool
{
    private readonly ITaskManager _taskManager;

    public TaskInspectTool(ITaskManager taskManager)
    {
        _taskManager = taskManager;
    }

    public string Name => "task_inspect";

    public string Description => "Inspect one task with recent events and worker transcript tail.";

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
            return new ToolExecutionResult(false, string.Empty, "task_inspect requires a 'taskId' string.");
        }

        var inspection = await _taskManager.InspectAsync(taskId, cancellationToken);
        if (inspection is null)
        {
            return new ToolExecutionResult(false, string.Empty, $"Task '{taskId}' was not found.");
        }

        return new ToolExecutionResult(true, JsonSerializer.Serialize(new
        {
            taskId = inspection.Task.Id,
            parentTaskId = inspection.Task.ParentTaskId,
            rootTaskId = inspection.Task.RootTaskId,
            depth = inspection.Task.Depth,
            provider = inspection.Task.Provider,
            status = inspection.Task.Status.ToString(),
            title = inspection.Task.Title,
            workerSessionId = inspection.Worker.WorkerSessionId,
            updatedAtUtc = inspection.Task.UpdatedAtUtc,
            summary = inspection.Task.Result?.Summary ?? inspection.Task.LastStatusMessage,
            progressPercent = inspection.Task.ProgressPercent,
            progressMessage = inspection.Task.ProgressMessage,
            recentEvents = inspection.RecentEvents,
            childTasks = inspection.Children.Select(child => new
            {
                taskId = child.Id,
                status = child.Status.ToString(),
                title = child.Title,
                updatedAtUtc = child.UpdatedAtUtc,
                summary = child.Result?.Summary ?? child.LastStatusMessage,
                progressPercent = child.ProgressPercent,
                progressMessage = child.ProgressMessage
            }),
            worker = inspection.Worker
        }));
    }
}
