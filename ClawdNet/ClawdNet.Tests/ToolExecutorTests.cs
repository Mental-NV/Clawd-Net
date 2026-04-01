using System.Text.Json.Nodes;
using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Models;
using ClawdNet.Runtime.Tools;
using ClawdNet.Tests.TestDoubles;

namespace ClawdNet.Tests;

public sealed class ToolExecutorTests
{
    [Fact]
    public async Task Unknown_tool_returns_failure()
    {
        var registry = new ToolRegistry([new EchoTool()]);
        var executor = new ToolExecutor(registry);

        var result = await executor.ExecuteAsync(new ToolExecutionRequest("missing", null, "x"), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("Unknown tool", result.Error);
    }

    [Fact]
    public async Task Shell_tool_runs_allowlisted_command()
    {
        var processRunner = new FakeProcessRunner
        {
            Handler = _ => new ProcessResult(0, "/tmp\n", string.Empty)
        };
        var tool = new ShellTool(processRunner);

        var result = await tool.ExecuteAsync(
            new ToolExecutionRequest("shell", new JsonObject { ["command"] = "pwd" }),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("/tmp", result.Output);
    }
}
