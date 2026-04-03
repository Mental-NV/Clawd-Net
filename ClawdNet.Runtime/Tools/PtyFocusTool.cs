using System.Text.Json.Nodes;
using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Models;

namespace ClawdNet.Runtime.Tools;

public sealed class PtyFocusTool : ITool
{
    private readonly IPtyManager _ptyManager;

    public PtyFocusTool(IPtyManager ptyManager)
    {
        _ptyManager = ptyManager;
    }

    public string Name => "pty_focus";

    public string Description => "Focus an existing PTY session for subsequent PTY reads and writes.";

    public ToolCategory Category => ToolCategory.Execute;

    public JsonObject InputSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["sessionId"] = new JsonObject { ["type"] = "string" }
        },
        ["required"] = new JsonArray("sessionId")
    };

    public async Task<ToolExecutionResult> ExecuteAsync(ToolExecutionRequest request, CancellationToken cancellationToken)
    {
        var sessionId = request.Input?["sessionId"]?.GetValue<string>()?.Trim();
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return new ToolExecutionResult(false, string.Empty, "pty_focus requires a 'sessionId' string.");
        }

        try
        {
            var state = await _ptyManager.FocusAsync(sessionId, cancellationToken);
            return new ToolExecutionResult(true, $"Focused PTY session {state.SessionId}: {state.Command}");
        }
        catch (Exception ex)
        {
            return new ToolExecutionResult(false, string.Empty, ex.Message);
        }
    }
}
