using ClawdNet.Core.Models;
using ClawdNet.Runtime.Tools;
using ClawdNet.Runtime.Sessions;
using ClawdNet.Terminal.Rendering;
using ClawdNet.Terminal.Repl;
using ClawdNet.Tests.TestDoubles;

namespace ClawdNet.Tests;

public sealed class ReplHostTests : IDisposable
{
    private readonly string _dataRoot = Path.Combine(Path.GetTempPath(), "clawdnet-repl-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Repl_starts_new_session_processes_prompt_and_exits()
    {
        var store = new JsonSessionStore(_dataRoot);
        var terminal = new FakeTerminalSession(["hello", "exit"]);
        var queryEngine = new FakeQueryEngine
        {
            Handler = async request =>
            {
                var session = await store.GetAsync(request.SessionId!, CancellationToken.None)
                    ?? throw new InvalidOperationException("Expected session to exist.");
                var updated = session with
                {
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                    Messages =
                    [
                        .. session.Messages,
                        new ConversationMessage("user", request.Prompt, DateTimeOffset.UtcNow),
                        new ConversationMessage("assistant", "hi there", DateTimeOffset.UtcNow)
                    ]
                };
                await store.SaveAsync(updated, CancellationToken.None);
                return new QueryExecutionResult(updated, "hi there", 1);
            }
        };
        var host = new ReplHost(terminal, store, queryEngine, new ConsoleTranscriptRenderer());

        var result = await host.RunAsync(new ReplLaunchOptions(), CancellationToken.None);
        var sessions = await store.ListAsync(CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Single(queryEngine.Requests);
        Assert.Contains("ClawdNet interactive mode", terminal.OutputLines[0]);
        Assert.Contains("ClawdNet: hi there", string.Join(Environment.NewLine, terminal.OutputLines));
        Assert.Contains("Exiting ClawdNet.", terminal.StatusLines.Last());
        Assert.Single(sessions);
    }

    [Fact]
    public async Task Repl_resumes_existing_session()
    {
        var store = new JsonSessionStore(_dataRoot);
        var existing = await store.CreateAsync("Resume me", "claude-sonnet-4-5", CancellationToken.None);
        var terminal = new FakeTerminalSession(["quit"]);
        var queryEngine = new FakeQueryEngine();
        var host = new ReplHost(terminal, store, queryEngine, new ConsoleTranscriptRenderer());

        var result = await host.RunAsync(new ReplLaunchOptions(existing.Id), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(queryEngine.Requests);
        Assert.Contains(existing.Id, terminal.StatusLines[0]);
    }

    [Fact]
    public async Task Repl_invalid_session_returns_failure()
    {
        var store = new JsonSessionStore(_dataRoot);
        var terminal = new FakeTerminalSession([]);
        var queryEngine = new FakeQueryEngine();
        var host = new ReplHost(terminal, store, queryEngine, new ConsoleTranscriptRenderer());

        var result = await host.RunAsync(new ReplLaunchOptions("missing"), CancellationToken.None);

        Assert.Equal(3, result.ExitCode);
        Assert.Contains("was not found", result.StdErr);
    }

    [Fact]
    public async Task Repl_prompts_for_permission_and_continues_after_denial()
    {
        var store = new JsonSessionStore(_dataRoot);
        var session = await store.CreateAsync("Resume me", "claude-sonnet-4-5", CancellationToken.None);
        var terminal = new FakeTerminalSession(["hello", "quit"], [false]);
        var queryEngine = new FakeQueryEngine
        {
            Handler = async request =>
            {
                var allowed = await request.ApprovalHandler!.ApproveAsync(
                    new FileWriteTool(),
                    new ToolCall("tool-1", "file_write", null),
                    new PermissionDecision(PermissionDecisionKind.Ask, "Write tool requires approval."),
                    CancellationToken.None);
                var existing = await store.GetAsync(request.SessionId!, CancellationToken.None) ?? session;
                var updated = existing with
                {
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                    Messages =
                    [
                        .. existing.Messages,
                        new ConversationMessage("user", request.Prompt, DateTimeOffset.UtcNow),
                        new ConversationMessage("permission", allowed ? "Allow: user approved tool execution." : "Deny: user denied tool execution.", DateTimeOffset.UtcNow, "file_write", "tool-1", !allowed),
                        new ConversationMessage("assistant", allowed ? "approved" : "denied", DateTimeOffset.UtcNow)
                    ]
                };
                await store.SaveAsync(updated, CancellationToken.None);
                return new QueryExecutionResult(updated, allowed ? "approved" : "denied", 1);
            }
        };
        var host = new ReplHost(terminal, store, queryEngine, new ConsoleTranscriptRenderer());

        var result = await host.RunAsync(new ReplLaunchOptions(session.Id), CancellationToken.None);
        var updatedSession = await store.GetAsync(session.Id, CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(terminal.Prompts, prompt => prompt.Contains("Allow file_write"));
        Assert.NotNull(updatedSession);
        Assert.Contains(updatedSession!.Messages, message => message.Role == "permission" && message.ToolName == "file_write" && message.IsError);
        Assert.Contains(updatedSession.Messages, message => message.Role == "assistant" && message.Content == "denied");
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataRoot))
        {
            Directory.Delete(_dataRoot, recursive: true);
        }
    }
}
