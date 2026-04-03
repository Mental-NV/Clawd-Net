using System.Text.Json.Nodes;
using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Models;

namespace ClawdNet.Runtime.Tools;

public sealed class PtyListTool : ITool
{
    private readonly IPtyManager _ptyManager;

    public PtyListTool(IPtyManager ptyManager)
    {
        _ptyManager = ptyManager;
    }

    public string Name => "pty_list";

    public string Description => "List PTY sessions and identify the current focused session.";

    public ToolCategory Category => ToolCategory.ReadOnly;

    public JsonObject InputSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject()
    };

    public async Task<ToolExecutionResult> ExecuteAsync(ToolExecutionRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var sessions = await _ptyManager.ListAsync(cancellationToken);
            if (sessions.Count == 0)
            {
                return new ToolExecutionResult(true, "No PTY sessions.");
            }

            var lines = sessions.Select(session =>
                $"{(session.IsCurrent ? "*" : "-")} session={session.SessionId} | running={session.IsRunning} | command={session.Command} | cwd={session.WorkingDirectory}");
            return new ToolExecutionResult(true, string.Join(Environment.NewLine, lines));
        }
        catch (Exception ex)
        {
            return new ToolExecutionResult(false, string.Empty, ex.Message);
        }
    }
}
