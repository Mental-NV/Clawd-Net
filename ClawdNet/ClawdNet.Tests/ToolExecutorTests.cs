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

    [Fact]
    public async Task Glob_tool_lists_matching_files()
    {
        var root = Path.Combine(Path.GetTempPath(), "clawdnet-glob", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "a.txt"), "a");
            await File.WriteAllTextAsync(Path.Combine(root, "b.cs"), "b");
            var tool = new GlobTool();

            var result = await tool.ExecuteAsync(
                new ToolExecutionRequest("glob", new JsonObject { ["path"] = root, ["pattern"] = "*.txt" }),
                CancellationToken.None);

            Assert.True(result.Success);
            Assert.Contains("a.txt", result.Output);
            Assert.DoesNotContain("b.cs", result.Output);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task File_write_tool_persists_content()
    {
        var root = Path.Combine(Path.GetTempPath(), "clawdnet-write", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "note.txt");
            var tool = new FileWriteTool();

            var result = await tool.ExecuteAsync(
                new ToolExecutionRequest("file_write", new JsonObject { ["path"] = path, ["content"] = "hello" }),
                CancellationToken.None);

            Assert.True(result.Success);
            Assert.Equal("hello", await File.ReadAllTextAsync(path));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task File_write_tool_syncs_with_lsp_client()
    {
        var root = Path.Combine(Path.GetTempPath(), "clawdnet-write-lsp", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "note.cs");
            var lspClient = new FakeLspClient();
            var tool = new FileWriteTool(lspClient);

            var result = await tool.ExecuteAsync(
                new ToolExecutionRequest("file_write", new JsonObject { ["path"] = path, ["content"] = "class A {}" }),
                CancellationToken.None);

            Assert.True(result.Success);
            Assert.Single(lspClient.SyncRequests);
            Assert.Equal(path, lspClient.SyncRequests[0].Path);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Pty_tools_round_trip_against_fake_manager()
    {
        var manager = new FakePtyManager();
        var registry = new ToolRegistry(
        [
            new PtyStartTool(manager),
            new PtyFocusTool(manager),
            new PtyListTool(manager),
            new PtyWriteTool(manager),
            new PtyReadTool(manager),
            new PtyCloseTool(manager)
        ]);
        var executor = new ToolExecutor(registry);

        var start = await executor.ExecuteAsync(
            new ToolExecutionRequest("pty_start", new JsonObject { ["command"] = "cat" }),
            CancellationToken.None);
        var write = await executor.ExecuteAsync(
            new ToolExecutionRequest("pty_write", new JsonObject { ["text"] = "hello\n" }),
            CancellationToken.None);
        var read = await executor.ExecuteAsync(
            new ToolExecutionRequest("pty_read", new JsonObject()),
            CancellationToken.None);
        var list = await executor.ExecuteAsync(
            new ToolExecutionRequest("pty_list", new JsonObject()),
            CancellationToken.None);
        var focus = await executor.ExecuteAsync(
            new ToolExecutionRequest("pty_focus", new JsonObject { ["sessionId"] = manager.State.CurrentSessionId }),
            CancellationToken.None);
        var close = await executor.ExecuteAsync(
            new ToolExecutionRequest("pty_close", new JsonObject()),
            CancellationToken.None);

        Assert.True(start.Success);
        Assert.True(write.Success);
        Assert.True(read.Success);
        Assert.True(list.Success);
        Assert.True(focus.Success);
        Assert.Contains("hello", read.Output);
        Assert.Contains("session=", list.Output);
        Assert.True(close.Success);
    }
}
