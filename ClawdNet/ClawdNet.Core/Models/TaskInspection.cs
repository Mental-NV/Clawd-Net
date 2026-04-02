namespace ClawdNet.Core.Models;

public sealed record TaskInspection(
    TaskRecord Task,
    IReadOnlyList<TaskEvent> RecentEvents,
    TaskWorkerSnapshot Worker);
