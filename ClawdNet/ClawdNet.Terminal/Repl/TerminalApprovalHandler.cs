using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Models;
using ClawdNet.Terminal.Abstractions;

namespace ClawdNet.Terminal.Repl;

public sealed class TerminalApprovalHandler : IToolApprovalHandler
{
    private readonly ITerminalSession _terminalSession;
    private readonly Action<TerminalActivityState, string?>? _setActivity;

    public TerminalApprovalHandler(
        ITerminalSession terminalSession,
        Action<TerminalActivityState, string?>? setActivity = null)
    {
        _terminalSession = terminalSession;
        _setActivity = setActivity;
    }

    public Task<bool> ApproveAsync(ITool tool, ToolCall toolCall, PermissionDecision decision, CancellationToken cancellationToken)
    {
        _setActivity?.Invoke(
            TerminalActivityState.AwaitingApproval,
            $"Awaiting approval for {tool.Name}: {decision.Reason}");
        return _terminalSession.ConfirmAsync(
            $"Allow {tool.Name} ({tool.Category})? {decision.Reason}",
            cancellationToken);
    }
}
