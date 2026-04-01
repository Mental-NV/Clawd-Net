using ClawdNet.Core.Models;

namespace ClawdNet.Core.Abstractions;

public interface IToolApprovalHandler
{
    Task<bool> ApproveAsync(ITool tool, ToolCall toolCall, PermissionDecision decision, CancellationToken cancellationToken);
}
