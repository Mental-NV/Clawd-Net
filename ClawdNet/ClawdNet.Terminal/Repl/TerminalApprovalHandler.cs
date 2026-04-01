using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Models;
using ClawdNet.Terminal.Abstractions;

namespace ClawdNet.Terminal.Repl;

public sealed class TerminalApprovalHandler : IToolApprovalHandler
{
    private readonly ITerminalSession _terminalSession;

    public TerminalApprovalHandler(ITerminalSession terminalSession)
    {
        _terminalSession = terminalSession;
    }

    public Task<bool> ApproveAsync(ITool tool, ToolCall toolCall, PermissionDecision decision, CancellationToken cancellationToken)
    {
        return _terminalSession.ConfirmAsync(
            $"Allow {tool.Name} ({tool.Category})? {decision.Reason}",
            cancellationToken);
    }
}
