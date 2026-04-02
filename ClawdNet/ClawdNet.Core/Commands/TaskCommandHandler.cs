using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Models;

namespace ClawdNet.Core.Commands;

public sealed class TaskCommandHandler : ICommandHandler
{
    public string Name => "task";

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
                $"{task.Id} | {task.Status} | {task.Title} | worker={task.WorkerSessionId} | {task.UpdatedAtUtc:O}");
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
                    $"WorkerSession: {task.WorkerSessionId}",
                    $"Model: {task.Model}",
                    $"UpdatedAtUtc: {task.UpdatedAtUtc:O}",
                    $"Summary: {task.Result?.Summary ?? task.LastStatusMessage ?? "(none)"}",
                    $"WorkerMessages: {inspection.Worker.MessageCount}",
                    $"WorkerUpdatedAtUtc: {inspection.Worker.UpdatedAtUtc:O}",
                    "RecentEvents:",
                    recentEvents,
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
