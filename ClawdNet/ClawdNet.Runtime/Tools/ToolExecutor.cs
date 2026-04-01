using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Models;

namespace ClawdNet.Runtime.Tools;

public sealed class ToolExecutor : IToolExecutor
{
    private readonly IToolRegistry _toolRegistry;

    public ToolExecutor(IToolRegistry toolRegistry)
    {
        _toolRegistry = toolRegistry;
    }

    public Task<ToolExecutionResult> ExecuteAsync(ToolExecutionRequest request, CancellationToken cancellationToken)
    {
        if (!_toolRegistry.TryGet(request.ToolName, out var tool) || tool is null)
        {
            return Task.FromResult(new ToolExecutionResult(false, string.Empty, $"Unknown tool '{request.ToolName}'."));
        }

        return tool.ExecuteAsync(request, cancellationToken);
    }
}
