namespace ClawdNet.Core.Models;

public sealed record CommandExecutionResult(int ExitCode, string StdOut = "", string StdErr = "")
{
    public static CommandExecutionResult Success(string stdOut = "") => new(0, stdOut);

    public static CommandExecutionResult Failure(string stdErr, int exitCode = 1) => new(exitCode, "", stdErr);
}
