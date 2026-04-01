using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Models;

namespace ClawdNet.Runtime.Tools;

public sealed class EchoTool : ITool
{
    public string Name => "echo";

    public Task<ToolExecutionResult> ExecuteAsync(ToolExecutionRequest request, CancellationToken cancellationToken)
    {
        return Task.FromResult(new ToolExecutionResult(true, request.Input));
    }
}
