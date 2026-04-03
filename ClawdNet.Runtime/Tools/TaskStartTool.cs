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
            ["parentTaskId"] = new JsonObject { ["type"] = "string" },
            ["parentSummary"] = new JsonObject { ["type"] = "string" },
            ["cwd"] = new JsonObject { ["type"] = "string" },
            ["provider"] = new JsonObject { ["type"] = "string" },
            ["model"] = new JsonObject { ["type"] = "string" },
            ["permissionMode"] = new JsonObject { ["type"] = "string" },
            ["maxDurationSeconds"] = new JsonObject { ["type"] = "integer" },
            ["dependsOnTaskIds"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" } }
        },
        ["required"] = new JsonArray("title", "goal")
    };

    public async Task<ToolExecutionResult> ExecuteAsync(ToolExecutionRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.SessionId))
        {
            return new ToolExecutionResult(false, string.Empty, "task_start requires a current session id.");
        }

        var title = request.Input?["title"]?.GetValue<string>()?.Trim();
        var goal = request.Input?["goal"]?.GetValue<string>()?.Trim();
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(goal))
        {
            return new ToolExecutionResult(false, string.Empty, "task_start requires 'title' and 'goal' strings.");
        }

        var currentTask = await _taskManager.GetByWorkerSessionIdAsync(request.SessionId, cancellationToken);
        var parentSessionId = currentTask?.ParentSessionId ?? request.SessionId;
        var parentTaskId = currentTask?.Id ?? request.Input?["parentTaskId"]?.GetValue<string>()?.Trim();
        var overrideMode = request.Input?["permissionMode"]?.GetValue<string>();
        var permissionMode = ParsePermissionMode(overrideMode) ?? request.PermissionMode;
        TaskRecord task;
        try
        {
            var maxDurationSeconds = request.Input?["maxDurationSeconds"]?.GetValue<int>();
            var dependsOnTaskIds = request.Input?["dependsOnTaskIds"] as JsonArray;
            var depIds = dependsOnTaskIds?
                .Where(e => e is not null)
                .Select(e => e!.GetValue<string>())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToArray();
            task = await _taskManager.StartAsync(
                new TaskRequest(
                    title,
                    goal,
                    parentSessionId,
                    parentTaskId,
                    request.Input?["parentSummary"]?.GetValue<string>(),
                    request.Input?["cwd"]?.GetValue<string>(),
                    request.Input?["model"]?.GetValue<string>(),
                    permissionMode,
                    Provider: request.Input?["provider"]?.GetValue<string>(),
                    MaxDurationSeconds: maxDurationSeconds,
                    DependsOnTaskIds: depIds),
                cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return new ToolExecutionResult(false, string.Empty, ex.Message);
        }

        return new ToolExecutionResult(true, JsonSerializer.Serialize(new
        {
            taskId = task.Id,
            parentTaskId = task.ParentTaskId,
            rootTaskId = task.RootTaskId,
            depth = task.Depth,
            provider = task.Provider,
            model = task.Model,
            status = task.Status.ToString(),
            title = task.Title,
            workerSessionId = task.WorkerSessionId,
            summary = task.LastStatusMessage,
            progressPercent = task.ProgressPercent,
            progressMessage = task.ProgressMessage,
            dependsOnTaskIds = task.DependsOnTaskIds ?? [],
            dependencyCount = task.DependsOnTaskIds?.Count ?? 0,
            childTaskCount = task.ChildTaskIds?.Count ?? 0,
            workerMessageCount = task.WorkerMessageCount,
            workerUpdatedAtUtc = task.WorkerUpdatedAtUtc
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
