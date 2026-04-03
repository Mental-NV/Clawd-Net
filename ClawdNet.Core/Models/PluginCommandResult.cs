namespace ClawdNet.Core.Models;

public sealed record PluginCommandResult(
    bool Success,
    string StdOut,
    string StdErr = "",
    int ExitCode = 0);
