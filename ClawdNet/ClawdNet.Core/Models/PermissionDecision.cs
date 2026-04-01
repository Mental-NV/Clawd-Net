namespace ClawdNet.Core.Models;

public sealed record PermissionDecision(
    PermissionDecisionKind Kind,
    string Reason);
