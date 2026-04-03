using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Models;

namespace ClawdNet.Runtime.Permissions;

public sealed class DefaultPermissionService : IPermissionService
{
    public PermissionDecision Evaluate(ITool tool, PermissionMode mode)
    {
        return mode switch
        {
            PermissionMode.BypassPermissions => new PermissionDecision(PermissionDecisionKind.Allow, "Bypass mode enabled."),
            PermissionMode.AcceptEdits => tool.Category switch
            {
                ToolCategory.ReadOnly => new PermissionDecision(PermissionDecisionKind.Allow, "Read-only tool allowed."),
                ToolCategory.Write => new PermissionDecision(PermissionDecisionKind.Allow, "Write tool allowed by acceptEdits mode."),
                ToolCategory.Execute => new PermissionDecision(PermissionDecisionKind.Ask, "Execute tool requires approval."),
                _ => new PermissionDecision(PermissionDecisionKind.Deny, "Tool denied.")
            },
            _ => tool.Category switch
            {
                ToolCategory.ReadOnly => new PermissionDecision(PermissionDecisionKind.Allow, "Read-only tool allowed."),
                ToolCategory.Write => new PermissionDecision(PermissionDecisionKind.Ask, "Write tool requires approval."),
                ToolCategory.Execute => new PermissionDecision(PermissionDecisionKind.Ask, "Execute tool requires approval."),
                _ => new PermissionDecision(PermissionDecisionKind.Deny, "Tool denied.")
            }
        };
    }
}
