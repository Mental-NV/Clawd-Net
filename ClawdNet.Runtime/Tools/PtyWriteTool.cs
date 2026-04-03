using System.Text.Json.Nodes;
using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Models;

namespace ClawdNet.Runtime.Tools;

public sealed class PtyWriteTool : ITool
{
    private readonly IPtyManager _ptyManager;

    public PtyWriteTool(IPtyManager ptyManager)
    {
        _ptyManager = ptyManager;
    }

    public string Name => "pty_write";

    public string Description => "Write input to the active PTY session.";

    public ToolCategory Category => ToolCategory.Execute;

    public JsonObject InputSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["text"] = new JsonObject { ["type"] = "string" },
            ["sessionId"] = new JsonObject { ["type"] = "string" }
        },
        ["required"] = new JsonArray("text")
    };

    public async Task<ToolExecutionResult> ExecuteAsync(ToolExecutionRequest request, CancellationToken cancellationToken)
    {
        var text = request.Input?["text"]?.GetValue<string>() ?? request.RawInput;
        var sessionId = request.Input?["sessionId"]?.GetValue<string>();
        if (string.IsNullOrEmpty(text))
        {
            return new ToolExecutionResult(false, string.Empty, "pty_write requires 'text'.");
        }

        try
        {
            var state = await _ptyManager.WriteAsync(text, sessionId, cancellationToken);
            return new ToolExecutionResult(true, $"Wrote {text.Length} chars to PTY session {state.SessionId}.");
        }
        catch (Exception ex)
        {
            return new ToolExecutionResult(false, string.Empty, ex.Message);
        }
    }
}
