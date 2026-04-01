using System.Text.Json.Nodes;
using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Models;

namespace ClawdNet.Runtime.Tools;

public sealed class ShellTool : ITool
{
    private static readonly HashSet<string> AllowedCommands = new(StringComparer.Ordinal)
    {
        "pwd",
        "echo",
        "cat"
    };

    private readonly IProcessRunner _processRunner;

    public ShellTool(IProcessRunner processRunner)
    {
        _processRunner = processRunner;
    }

    public string Name => "shell";

    public string Description => "Run a small allowlisted shell command.";

    public JsonObject InputSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["command"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "One allowlisted shell command."
            }
        },
        ["required"] = new JsonArray("command")
    };

    public async Task<ToolExecutionResult> ExecuteAsync(ToolExecutionRequest request, CancellationToken cancellationToken)
    {
        var command = request.Input?["command"]?.GetValue<string>()?.Trim();
        if (string.IsNullOrWhiteSpace(command))
        {
            return new ToolExecutionResult(false, string.Empty, "shell requires a 'command' string.");
        }

        var verb = command.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries)[0];
        if (!AllowedCommands.Contains(verb))
        {
            return new ToolExecutionResult(false, string.Empty, $"shell command '{verb}' is not allowed.");
        }

        var result = await _processRunner.RunAsync(new ProcessRequest("/bin/zsh", $"-lc \"{command.Replace("\"", "\\\"")}\""), cancellationToken);
        return result.ExitCode == 0
            ? new ToolExecutionResult(true, result.StdOut.TrimEnd())
            : new ToolExecutionResult(false, string.Empty, string.IsNullOrWhiteSpace(result.StdErr) ? $"shell exited with code {result.ExitCode}." : result.StdErr.Trim());
    }
}
