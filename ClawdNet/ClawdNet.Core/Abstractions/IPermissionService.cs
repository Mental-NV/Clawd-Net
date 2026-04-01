using ClawdNet.Core.Models;

namespace ClawdNet.Core.Abstractions;

public interface IPermissionService
{
    PermissionDecision Evaluate(ITool tool, PermissionMode mode);
}
