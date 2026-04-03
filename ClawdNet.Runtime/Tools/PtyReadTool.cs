using System.Text.Json.Nodes;
using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Models;

namespace ClawdNet.Runtime.Tools;

public sealed class PtyReadTool : ITool
{
    private readonly IPtyManager _ptyManager;

    public PtyReadTool(IPtyManager ptyManager)
    {
        _ptyManager = ptyManager;
    }

    public string Name => "pty_read";

    public string Description => "Read the active PTY session state and recent output.";

    public ToolCategory Category => ToolCategory.ReadOnly;

    public JsonObject InputSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["sessionId"] = new JsonObject { ["type"] = "string" }
        }
    };

    public async Task<ToolExecutionResult> ExecuteAsync(ToolExecutionRequest request, CancellationToken cancellationToken)
    {
        PtySessionState? state;
        var sessionId = request.Input?["sessionId"]?.GetValue<string>();
        try
        {
            state = await _ptyManager.ReadAsync(sessionId, cancellationToken);
        }
        catch (Exception ex)
        {
            return new ToolExecutionResult(false, string.Empty, ex.Message);
        }

        if (state is null)
        {
            return new ToolExecutionResult(false, string.Empty, "No active PTY session.");
        }

        var output = FormatState(state);

        // Append transcript info if available
        try
        {
            var transcript = await _ptyManager.GetTranscriptAsync(state.SessionId, tailCount: null, cancellationToken);
            if (transcript.Count > 0)
            {
                output += $"{Environment.NewLine}{Environment.NewLine}transcript={transcript.Count} chunks persisted";
            }
        }
        catch
        {
            // Suppress transcript read failures
        }

        return new ToolExecutionResult(true, output);
    }

    public static string FormatState(PtySessionState state)
    {
        var lines = new List<string>
        {
            $"session={state.SessionId}",
            $"command={state.Command}",
            $"cwd={state.WorkingDirectory}",
            $"running={state.IsRunning}",
            $"exitCode={(state.ExitCode.HasValue ? state.ExitCode.Value.ToString() : "n/a")}",
            $"duration={FormatDuration(state.Duration)}",
            $"lines={state.OutputLineCount}"
        };
        if (state.IsBackground)
        {
            lines.Add("background=true");
        }
        if (state.Timeout.HasValue)
        {
            lines.Add($"timeout={FormatDuration(state.Timeout.Value)}");
        }
        if (state.IsOutputClipped)
        {
            lines.Add("outputClipped=true");
        }

        lines.Add(string.Empty);
        lines.Add(string.IsNullOrWhiteSpace(state.RecentOutput) ? "(no output yet)" : state.RecentOutput.TrimEnd());
        return string.Join(Environment.NewLine, lines).TrimEnd();
    }

    private static string FormatDuration(TimeSpan ts)
    {
        if (ts.TotalMinutes < 1)
        {
            return $"{(int)ts.TotalSeconds}s";
        }
        return $"{(int)ts.TotalMinutes}m{(int)ts.Seconds}s";
    }
}
