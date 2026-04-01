using System.Text.Json;
using System.Text.Json.Nodes;
using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Models;

namespace ClawdNet.Runtime.Tools;

public sealed class TaskCancelTool : ITool
{
    private readonly ITaskManager _taskManager;

    public TaskCancelTool(ITaskManager taskManager)
    {
        _taskManager = taskManager;
    }

    public string Name => "task_cancel";

    public string Description => "Cancel one running worker task.";

    public ToolCategory Category => ToolCategory.Execute;

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
            return new ToolExecutionResult(false, string.Empty, "task_cancel requires a 'taskId' string.");
        }

        var task = await _taskManager.CancelAsync(taskId, cancellationToken);
        if (task is null)
        {
            return new ToolExecutionResult(false, string.Empty, $"Task '{taskId}' was not found.");
        }

        return new ToolExecutionResult(true, JsonSerializer.Serialize(new
        {
            taskId = task.Id,
            status = task.Status.ToString(),
            summary = task.Result?.Summary ?? task.LastStatusMessage
        }));
    }
}
