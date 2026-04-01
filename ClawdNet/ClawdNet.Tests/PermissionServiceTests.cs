using ClawdNet.Core.Models;
using ClawdNet.Runtime.Permissions;
using ClawdNet.Runtime.Tools;
using ClawdNet.Tests.TestDoubles;

namespace ClawdNet.Tests;

public sealed class PermissionServiceTests
{
    [Fact]
    public void Default_mode_allows_readonly_tools()
    {
        var service = new DefaultPermissionService();

        var decision = service.Evaluate(new FileReadTool(), PermissionMode.Default);

        Assert.Equal(PermissionDecisionKind.Allow, decision.Kind);
    }

    [Fact]
    public void Default_mode_requires_approval_for_write_tools()
    {
        var service = new DefaultPermissionService();

        var decision = service.Evaluate(new FileWriteTool(), PermissionMode.Default);

        Assert.Equal(PermissionDecisionKind.Ask, decision.Kind);
    }

    [Fact]
    public void Bypass_mode_allows_shell_execution()
    {
        var service = new DefaultPermissionService();
        var tool = new ShellTool(new FakeProcessRunner());

        var decision = service.Evaluate(tool, PermissionMode.BypassPermissions);

        Assert.Equal(PermissionDecisionKind.Allow, decision.Kind);
    }

    [Fact]
    public void Default_mode_requires_approval_for_pty_start()
    {
        var service = new DefaultPermissionService();
        var tool = new PtyStartTool(new FakePtyManager());

        var decision = service.Evaluate(tool, PermissionMode.Default);

        Assert.Equal(PermissionDecisionKind.Ask, decision.Kind);
    }

    [Fact]
    public void Default_mode_allows_pty_read()
    {
        var service = new DefaultPermissionService();
        var tool = new PtyReadTool(new FakePtyManager());

        var decision = service.Evaluate(tool, PermissionMode.Default);

        Assert.Equal(PermissionDecisionKind.Allow, decision.Kind);
    }

    [Fact]
    public void Default_mode_requires_approval_for_task_start()
    {
        var service = new DefaultPermissionService();
        var tool = new TaskStartTool(new FakeTaskManager());

        var decision = service.Evaluate(tool, PermissionMode.Default);

        Assert.Equal(PermissionDecisionKind.Ask, decision.Kind);
    }

    [Fact]
    public void Accept_edits_still_requires_approval_for_task_start()
    {
        var service = new DefaultPermissionService();
        var tool = new TaskStartTool(new FakeTaskManager());

        var decision = service.Evaluate(tool, PermissionMode.AcceptEdits);

        Assert.Equal(PermissionDecisionKind.Ask, decision.Kind);
    }
}
