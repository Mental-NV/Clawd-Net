using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Models;

namespace ClawdNet.Core.Commands;

public sealed class TaskCommandHandler : ICommandHandler
{
    public string Name => "task";

    public string HelpSummary => "List, inspect, and cancel worker tasks";

    public string HelpText => """
Usage: clawdnet task list
       clawdnet task show <id>
       clawdnet task cancel <id>

Manage delegated worker tasks.

Commands:
  list             List all tasks
  show <id>        Show task details including worker transcript tail
  cancel <id>      Cancel a running task

Examples:
  clawdnet task list
  clawdnet task show task-123
  clawdnet task cancel task-456
""";

    public bool CanHandle(CommandRequest request)
    {
        return request.Arguments.Count >= 2
            && string.Equals(request.Arguments[0], "task", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<CommandExecutionResult> ExecuteAsync(
        CommandContext context,
        CommandRequest request,
        CancellationToken cancellationToken)
    {
        var action = request.Arguments[1];
        if (string.Equals(action, "list", StringComparison.OrdinalIgnoreCase))
        {
            var tasks = await context.TaskManager.ListAsync(cancellationToken);
            if (tasks.Count == 0)
            {
                return CommandExecutionResult.Success("No tasks found.");
            }

            var lines = tasks.Select(task =>
                $"{task.Id} | {task.Status} | depth={task.Depth} | parentTask={task.ParentTaskId ?? "(root)"} | children={task.ChildTaskIds?.Count ?? 0} | {task.Title} | provider={task.Provider} | model={task.Model} | worker={task.WorkerSessionId} | {task.UpdatedAtUtc:O}");
            return CommandExecutionResult.Success(string.Join(Environment.NewLine, lines));
        }

        if (string.Equals(action, "show", StringComparison.OrdinalIgnoreCase) && request.Arguments.Count >= 3)
        {
            var inspection = await context.TaskManager.InspectAsync(request.Arguments[2], cancellationToken);
            if (inspection is null)
            {
                return CommandExecutionResult.Failure($"Task '{request.Arguments[2]}' was not found.", 3);
            }

            var task = inspection.Task;
            var recentEvents = inspection.RecentEvents.Count == 0
                ? "(none)"
                : string.Join(Environment.NewLine, inspection.RecentEvents.Select(taskEvent => $"{taskEvent.TimestampUtc:O} | {taskEvent.Status} | {taskEvent.Message}"));
            var output = string.Join(
                Environment.NewLine,
                [
                    $"Task: {task.Id}",
                    $"Status: {task.Status}",
                    $"Title: {task.Title}",
                    $"ParentSession: {task.ParentSessionId}",
                    $"ParentTask: {task.ParentTaskId ?? "(root)"}",
                    $"RootTask: {task.RootTaskId ?? task.Id}",
                    $"Depth: {task.Depth}",
                    $"WorkerSession: {task.WorkerSessionId}",
                    $"Provider: {task.Provider}",
                    $"Model: {task.Model}",
                    $"UpdatedAtUtc: {task.UpdatedAtUtc:O}",
                    $"Summary: {task.Result?.Summary ?? task.LastStatusMessage ?? "(none)"}",
                    $"ChildTasks: {inspection.Children.Count}",
                    $"WorkerMessages: {inspection.Worker.MessageCount}",
                    $"WorkerUpdatedAtUtc: {inspection.Worker.UpdatedAtUtc:O}",
                    "RecentEvents:",
                    recentEvents,
                    "ChildTaskSummary:",
                    inspection.Children.Count == 0
                        ? "(none)"
                        : string.Join(Environment.NewLine, inspection.Children.Select(child => $"{child.Id} | {child.Status} | {child.Title} | {child.UpdatedAtUtc:O}")),
                    "WorkerTranscriptTail:",
                    string.IsNullOrWhiteSpace(inspection.Worker.TranscriptTail) ? "(none)" : inspection.Worker.TranscriptTail
                ]);
            return CommandExecutionResult.Success(output);
        }

        if (string.Equals(action, "cancel", StringComparison.OrdinalIgnoreCase) && request.Arguments.Count >= 3)
        {
            var task = await context.TaskManager.CancelAsync(request.Arguments[2], cancellationToken);
            if (task is null)
            {
                return CommandExecutionResult.Failure($"Task '{request.Arguments[2]}' was not found.", 3);
            }

            return CommandExecutionResult.Success($"Canceled task {task.Id}: {task.Status} | {task.Result?.Summary ?? task.LastStatusMessage}");
        }

        return CommandExecutionResult.Failure("Supported task commands: task list, task show <id>, task cancel <id>.");
    }
}
