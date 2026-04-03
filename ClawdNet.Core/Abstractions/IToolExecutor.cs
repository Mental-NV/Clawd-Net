using ClawdNet.Core.Models;

namespace ClawdNet.Core.Abstractions;

public interface IToolExecutor
{
    Task<ToolExecutionResult> ExecuteAsync(ToolExecutionRequest request, CancellationToken cancellationToken);
}
