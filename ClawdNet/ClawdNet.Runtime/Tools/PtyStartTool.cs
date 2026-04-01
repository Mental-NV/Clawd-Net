using System.Text.Json.Nodes;
using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Models;

namespace ClawdNet.Runtime.Tools;

public sealed class PtyStartTool : ITool
{
    private static readonly HashSet<string> AllowedCommands = new(StringComparer.Ordinal)
    {
        "cat",
        "python",
        "python3",
        "sh",
        "bash"
    };

    private readonly IPtyManager _ptyManager;

    public PtyStartTool(IPtyManager ptyManager)
    {
        _ptyManager = ptyManager;
    }

    public string Name => "pty_start";

    public string Description => "Start one interactive PTY-backed command session.";

    public ToolCategory Category => ToolCategory.Execute;

    public JsonObject InputSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["command"] = new JsonObject { ["type"] = "string" },
            ["cwd"] = new JsonObject { ["type"] = "string" }
        },
        ["required"] = new JsonArray("command")
    };

    public async Task<ToolExecutionResult> ExecuteAsync(ToolExecutionRequest request, CancellationToken cancellationToken)
    {
        var command = request.Input?["command"]?.GetValue<string>()?.Trim();
        var cwd = request.Input?["cwd"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(command))
        {
            return new ToolExecutionResult(false, string.Empty, "pty_start requires a 'command' string.");
        }

        var verb = command.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries)[0];
        if (!AllowedCommands.Contains(verb))
        {
            return new ToolExecutionResult(false, string.Empty, $"pty command '{verb}' is not allowed.");
        }

        try
        {
            var state = await _ptyManager.StartAsync(command, cwd, cancellationToken);
            return new ToolExecutionResult(true, FormatState(state));
        }
        catch (Exception ex)
        {
            return new ToolExecutionResult(false, string.Empty, ex.Message);
        }
    }

    private static string FormatState(PtySessionState state)
    {
        return $"PTY session {state.SessionId} started: {state.Command}{Environment.NewLine}cwd={state.WorkingDirectory}";
    }
}
