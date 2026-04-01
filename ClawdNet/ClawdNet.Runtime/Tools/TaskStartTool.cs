using System.Text.Json;
using System.Text.Json.Nodes;
using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Models;

namespace ClawdNet.Runtime.Tools;

public sealed class TaskStartTool : ITool
{
    private readonly ITaskManager _taskManager;

    public TaskStartTool(ITaskManager taskManager)
    {
        _taskManager = taskManager;
    }

    public string Name => "task_start";

    public string Description => "Start one background worker task linked to the current parent session.";

    public ToolCategory Category => ToolCategory.Execute;

    public JsonObject InputSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["title"] = new JsonObject { ["type"] = "string" },
            ["goal"] = new JsonObject { ["type"] = "string" },
            ["parentSummary"] = new JsonObject { ["type"] = "string" },
            ["cwd"] = new JsonObject { ["type"] = "string" },
            ["model"] = new JsonObject { ["type"] = "string" },
            ["permissionMode"] = new JsonObject { ["type"] = "string" }
        },
        ["required"] = new JsonArray("title", "goal")
    };

    public async Task<ToolExecutionResult> ExecuteAsync(ToolExecutionRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.SessionId))
        {
            return new ToolExecutionResult(false, string.Empty, "task_start requires a parent session id.");
        }

        var title = request.Input?["title"]?.GetValue<string>()?.Trim();
        var goal = request.Input?["goal"]?.GetValue<string>()?.Trim();
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(goal))
        {
            return new ToolExecutionResult(false, string.Empty, "task_start requires 'title' and 'goal' strings.");
        }

        var overrideMode = request.Input?["permissionMode"]?.GetValue<string>();
        var permissionMode = ParsePermissionMode(overrideMode) ?? request.PermissionMode;
        var task = await _taskManager.StartAsync(
            new TaskRequest(
                title,
                goal,
                request.SessionId,
                request.Input?["parentSummary"]?.GetValue<string>(),
                request.Input?["cwd"]?.GetValue<string>(),
                request.Input?["model"]?.GetValue<string>(),
                permissionMode),
            cancellationToken);

        return new ToolExecutionResult(true, JsonSerializer.Serialize(new
        {
            taskId = task.Id,
            status = task.Status.ToString(),
            title = task.Title,
            workerSessionId = task.WorkerSessionId,
            summary = task.LastStatusMessage
        }));
    }

    private static PermissionMode? ParsePermissionMode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.ToLowerInvariant() switch
        {
            "default" => PermissionMode.Default,
            "acceptedits" => PermissionMode.AcceptEdits,
            "accept-edits" => PermissionMode.AcceptEdits,
            "bypasspermissions" => PermissionMode.BypassPermissions,
            "bypass-permissions" => PermissionMode.BypassPermissions,
            "bypass" => PermissionMode.BypassPermissions,
            _ => null
        };
    }
}
