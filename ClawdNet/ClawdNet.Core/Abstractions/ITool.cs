using ClawdNet.Core.Models;

namespace ClawdNet.Core.Abstractions;

public interface ITool
{
    string Name { get; }

    Task<ToolExecutionResult> ExecuteAsync(ToolExecutionRequest request, CancellationToken cancellationToken);
}
