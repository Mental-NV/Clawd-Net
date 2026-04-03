namespace ClawdNet.Core.Models;

/// <summary>
/// Tracks in-memory health metrics for a loaded plugin.
/// </summary>
public sealed record PluginHealthMetrics
{
    /// <summary>Number of tool invocations since the plugin was loaded.</summary>
    public int ToolInvocationCount { get; init; }

    /// <summary>Timestamp of the last tool invocation.</summary>
    public DateTimeOffset? LastToolInvocationUtc { get; init; }

    /// <summary>Number of command invocations since the plugin was loaded.</summary>
    public int CommandInvocationCount { get; init; }

    /// <summary>Timestamp of the last command invocation.</summary>
    public DateTimeOffset? LastCommandInvocationUtc { get; init; }

    /// <summary>Total number of hook invocations since the plugin was loaded.</summary>
    public int HookInvocationCount { get; init; }

    /// <summary>Number of successful hook invocations.</summary>
    public int HookSuccessCount { get; init; }

    /// <summary>Number of failed hook invocations.</summary>
    public int HookFailureCount { get; init; }

    /// <summary>Timestamp of the last plugin activity (tool, command, or hook).</summary>
    public DateTimeOffset? LastActivityUtc { get; init; }

    /// <summary>Whether the plugin is healthy (no hook failures).</summary>
    public bool IsHealthy => HookFailureCount == 0;

    /// <summary>Health status label.</summary>
    public string HealthStatus => HookFailureCount switch
    {
        0 when HookInvocationCount > 0 => "healthy",
        0 => "idle",
        > 0 when HookSuccessCount > HookFailureCount => "degraded",
        _ => "errors"
    };

    /// <summary>Creates a new health metrics instance with an incremented tool invocation count.</summary>
    public PluginHealthMetrics WithToolInvocation() => this with
    {
        ToolInvocationCount = ToolInvocationCount + 1,
        LastToolInvocationUtc = DateTimeOffset.UtcNow,
        LastActivityUtc = DateTimeOffset.UtcNow
    };

    /// <summary>Creates a new health metrics instance with an incremented command invocation count.</summary>
    public PluginHealthMetrics WithCommandInvocation() => this with
    {
        CommandInvocationCount = CommandInvocationCount + 1,
        LastCommandInvocationUtc = DateTimeOffset.UtcNow,
        LastActivityUtc = DateTimeOffset.UtcNow
    };

    /// <summary>Creates a new health metrics instance with an incremented hook invocation count and success/failure tracking.</summary>
    public PluginHealthMetrics WithHookInvocation(bool succeeded) => this with
    {
        HookInvocationCount = HookInvocationCount + 1,
        HookSuccessCount = HookSuccessCount + (succeeded ? 1 : 0),
        HookFailureCount = HookFailureCount + (succeeded ? 0 : 1),
        LastActivityUtc = DateTimeOffset.UtcNow
    };
}
