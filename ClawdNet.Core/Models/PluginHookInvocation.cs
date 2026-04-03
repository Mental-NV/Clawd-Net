namespace ClawdNet.Core.Models;

public sealed record PluginHookInvocation(
    PluginHookKind Kind,
    string? SessionId = null,
    string? TaskId = null,
    string? WorkingDirectory = null,
    object? Payload = null);
