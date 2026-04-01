using ClawdNet.Core.Models;
using ClawdNet.Runtime.Tools;

namespace ClawdNet.Tests;

public sealed class ToolExecutorTests
{
    [Fact]
    public async Task Unknown_tool_returns_failure()
    {
        var registry = new ToolRegistry([new EchoTool()]);
        var executor = new ToolExecutor(registry);

        var result = await executor.ExecuteAsync(new ToolExecutionRequest("missing", "x"), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("Unknown tool", result.Error);
    }
}
