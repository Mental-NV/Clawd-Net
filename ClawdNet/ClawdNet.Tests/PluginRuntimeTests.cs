using ClawdNet.Core.Models;
using ClawdNet.Runtime.Plugins;
using ClawdNet.Tests.TestDoubles;

namespace ClawdNet.Tests;

public sealed class PluginRuntimeTests
{
    [Fact]
    public async Task Plugin_runtime_executes_plugin_defined_command()
    {
        var pluginCatalog = new FakePluginCatalog
        {
            Plugins =
            [
                new PluginDefinition(
                    "demo",
                    "demo",
                    "/tmp/demo",
                    true,
                    new PluginManifest(
                        "demo",
                        "1.0.0",
                        true,
                        [],
                        [],
                        [
                            new PluginCommandDefinition(
                                "demo-run",
                                "python3",
                                ["command.py"],
                                new Dictionary<string, string>(),
                                PluginExecutionMode.Subprocess,
                                true)
                        ],
                        []),
                    [])
            ]
        };
        var processRunner = new FakeProcessRunner
        {
            Handler = request => new ClawdNet.Core.Abstractions.ProcessResult(0, "{\"stdout\":\"plugin ok\",\"exitCode\":0}", string.Empty)
        };
        var runtime = new PluginRuntime(pluginCatalog, processRunner, ["ask", "plugin"]);

        var result = await runtime.TryExecuteCommandAsync(new CommandRequest(["demo-run", "arg1"]), CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result!.Success);
        Assert.Equal("plugin ok", result.StdOut);
        Assert.Single(processRunner.Requests);
        Assert.Contains("\"command\":\"demo-run\"", processRunner.Requests[0].StandardInput);
    }

    [Fact]
    public async Task Plugin_runtime_invokes_non_blocking_hooks_and_returns_failures()
    {
        var pluginCatalog = new FakePluginCatalog
        {
            Plugins =
            [
                new PluginDefinition(
                    "demo",
                    "demo",
                    "/tmp/demo",
                    true,
                    new PluginManifest(
                        "demo",
                        "1.0.0",
                        true,
                        [],
                        [],
                        [],
                        [
                            new PluginHookDefinition(
                                PluginHookKind.AfterQuery,
                                "python3",
                                ["hook.py"],
                                new Dictionary<string, string>(),
                                PluginExecutionMode.Subprocess,
                                true,
                                false)
                        ]),
                    [])
            ]
        };
        var processRunner = new FakeProcessRunner
        {
            Handler = _ => new ClawdNet.Core.Abstractions.ProcessResult(1, "{\"message\":\"hook failed\",\"exitCode\":1}", string.Empty)
        };
        var runtime = new PluginRuntime(pluginCatalog, processRunner, ["ask", "plugin"]);

        var results = await runtime.InvokeHooksAsync(new PluginHookInvocation(PluginHookKind.AfterQuery, "session-1", null, "/tmp", new { ok = true }), CancellationToken.None);

        Assert.Single(results);
        Assert.False(results[0].Success);
        Assert.Equal("hook failed", results[0].Message);
    }
}
