using System.Text.Json.Nodes;
using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Models;

namespace ClawdNet.Runtime.Tools;

public sealed class PtyCloseTool : ITool
{
    private readonly IPtyManager _ptyManager;

    public PtyCloseTool(IPtyManager ptyManager)
    {
        _ptyManager = ptyManager;
    }

    public string Name => "pty_close";

    public string Description => "Close the active PTY session.";

    public ToolCategory Category => ToolCategory.Execute;

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
            state = await _ptyManager.CloseAsync(sessionId, cancellationToken);
        }
        catch (Exception ex)
        {
            return new ToolExecutionResult(false, string.Empty, ex.Message);
        }

        if (state is null)
        {
            return new ToolExecutionResult(false, string.Empty, "No active PTY session.");
        }

        return new ToolExecutionResult(true, $"Closed PTY session {state.SessionId}.");
    }
}
