using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Commands;
using ClawdNet.Core.Models;
using ClawdNet.Runtime.FeatureGates;
using ClawdNet.Tests.TestDoubles;

namespace ClawdNet.Tests;

public sealed class HelpCommandHandlerTests
{
    [Fact]
    public void Can_handle_root_help_flag()
    {
        var handler = CreateHandler();
        Assert.True(handler.CanHandle(new CommandRequest(["--help"])));
        Assert.True(handler.CanHandle(new CommandRequest(["-h"])));
    }

    [Fact]
    public void Can_handle_command_help_flag()
    {
        var handler = CreateHandler();
        Assert.True(handler.CanHandle(new CommandRequest(["ask", "--help"])));
        Assert.True(handler.CanHandle(new CommandRequest(["provider", "-h"])));
    }

    [Fact]
    public void Cannot_handle_without_help_flag()
    {
        var handler = CreateHandler();
        Assert.False(handler.CanHandle(new CommandRequest(["ask", "hello"])));
        Assert.False(handler.CanHandle(new CommandRequest(["provider", "list"])));
    }

    [Fact]
    public async Task Execute_returns_root_help_for_root_flag()
    {
        var handler = CreateHandler();
        var result = await handler.ExecuteAsync(
            CreateContext(),
            new CommandRequest(["--help"]),
            CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("ClawdNet", result.StdOut);
        Assert.Contains("Usage:", result.StdOut);
        Assert.Contains("ask", result.StdOut);
        Assert.Contains("provider", result.StdOut);
    }

    [Fact]
    public async Task Execute_returns_command_help_for_known_command()
    {
        var handler = CreateHandler();
        var result = await handler.ExecuteAsync(
            CreateContext(),
            new CommandRequest(["ask", "--help"]),
            CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Usage: clawdnet ask", result.StdOut);
        Assert.Contains("--session", result.StdOut);
        Assert.Contains("--json", result.StdOut);
    }

    [Fact]
    public async Task Execute_returns_root_help_for_unknown_command_with_help_flag()
    {
        var handler = CreateHandler();
        var result = await handler.ExecuteAsync(
            CreateContext(),
            new CommandRequest(["nonexistent", "--help"]),
            CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("ClawdNet", result.StdOut);
    }

    private static HelpCommandHandler CreateHandler()
    {
        ICommandHandler[] handlers =
        [
            new AskCommandHandler(),
            new ProviderCommandHandler(),
            new VersionCommandHandler()
        ];
        return new HelpCommandHandler(handlers);
    }

    private static CommandContext CreateContext()
    {
        return new CommandContext(
            new DictionaryFeatureGate(),
            new TestToolRegistry(),
            new TestToolExecutor(),
            new TestConversationStore(),
            new TestTaskStore(),
            new FakeTaskManager(),
            new FakeQueryEngine(),
            new FakeProviderCatalog(),
            new FakeMcpClient(),
            new FakeLspClient(),
            new FakePluginCatalog(),
            new FakePluginRuntime(),
            new FakePlatformLauncher(),
            new TestPermissionService(),
            new TestTranscriptRenderer(),
            "1.0.0");
    }
}

public sealed class RootPositionalPromptTests
{
    [Fact]
    public void Single_non_flag_argument_is_treated_as_prompt()
    {
        // This mirrors TryParseRootPositionalPrompt behavior in AppHost
        var args = new[] { "Summarize this project" };
        Assert.True(IsRootPositionalPrompt(args, out var prompt));
        Assert.Equal("Summarize this project", prompt);
    }

    [Fact]
    public void Flag_argument_is_not_treated_as_prompt()
    {
        var args = new[] { "--help" };
        Assert.False(IsRootPositionalPrompt(args, out _));
    }

    [Fact]
    public void Multiple_args_with_flags_are_not_treated_as_prompt()
    {
        var args = new[] { "--provider", "anthropic" };
        Assert.False(IsRootPositionalPrompt(args, out _));
    }

    [Fact]
    public void Empty_args_are_not_treated_as_prompt()
    {
        var args = Array.Empty<string>();
        Assert.False(IsRootPositionalPrompt(args, out _));
    }

    private static bool IsRootPositionalPrompt(IReadOnlyList<string> args, out string? prompt)
    {
        prompt = null;
        if (args.Count == 1 && !args[0].StartsWith("-", StringComparison.Ordinal))
        {
            prompt = args[0];
            return true;
        }

        return false;
    }
}

public sealed class PrintModeTests
{
    [Fact]
    public void Short_flag_extracts_prompt()
    {
        var args = new[] { "-p", "hello world" };
        Assert.True(IsPrintMode(args, out var prompt));
        Assert.Equal("hello world", prompt);
    }

    [Fact]
    public void Long_flag_extracts_prompt()
    {
        var args = new[] { "--print", "hello world" };
        Assert.True(IsPrintMode(args, out var prompt));
        Assert.Equal("hello world", prompt);
    }

    [Fact]
    public void Flag_with_additional_args_joins_them()
    {
        var args = new[] { "-p", "multi", "word", "prompt" };
        Assert.True(IsPrintMode(args, out var prompt));
        Assert.Equal("multi word prompt", prompt);
    }

    [Fact]
    public void Returns_false_without_print_flag()
    {
        var args = new[] { "ask", "hello" };
        Assert.False(IsPrintMode(args, out _));
    }

    [Fact]
    public void Returns_false_with_empty_prompt()
    {
        var args = new[] { "-p" };
        Assert.False(IsPrintMode(args, out _));
    }

    private static bool IsPrintMode(IReadOnlyList<string> args, out string? prompt)
    {
        prompt = null;
        var printIndex = -1;

        for (var i = 0; i < args.Count; i++)
        {
            if (args[i] is "-p" or "--print")
            {
                printIndex = i;
                break;
            }
        }

        if (printIndex < 0)
        {
            return false;
        }

        if (printIndex + 1 < args.Count)
        {
            prompt = string.Join(' ', args.Skip(printIndex + 1)).Trim();
        }

        return !string.IsNullOrWhiteSpace(prompt);
    }
}

// Minimal test doubles for help handler context
file sealed class TestToolRegistry : ClawdNet.Core.Abstractions.IToolRegistry
{
    private readonly List<ClawdNet.Core.Abstractions.ITool> _tools = new();
    public IReadOnlyCollection<ClawdNet.Core.Abstractions.ITool> Tools => _tools;
    public void Register(ClawdNet.Core.Abstractions.ITool tool) => _tools.Add(tool);
    public void RegisterRange(IEnumerable<ClawdNet.Core.Abstractions.ITool> tools) => _tools.AddRange(tools);
    public void UnregisterWhere(Func<ClawdNet.Core.Abstractions.ITool, bool> predicate) => _tools.RemoveAll(t => predicate(t));
    public bool TryGet(string name, out ClawdNet.Core.Abstractions.ITool? tool)
    {
        tool = _tools.FirstOrDefault(t => t.Name == name);
        return tool is not null;
    }
}

file sealed class TestToolExecutor : ClawdNet.Core.Abstractions.IToolExecutor
{
    public Task<ClawdNet.Core.Models.ToolExecutionResult> ExecuteAsync(ClawdNet.Core.Models.ToolExecutionRequest request, CancellationToken cancellationToken)
    {
        return Task.FromResult(new ClawdNet.Core.Models.ToolExecutionResult(true, "executed"));
    }
}

file sealed class TestConversationStore : ClawdNet.Core.Abstractions.IConversationStore
{
    public Task<ClawdNet.Core.Models.ConversationSession> CreateAsync(string? title, string model, CancellationToken cancellationToken, string? provider = null)
    {
        return Task.FromResult(new ClawdNet.Core.Models.ConversationSession(
            Guid.NewGuid().ToString("N"), title ?? "", model, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, [], provider ?? "anthropic"));
    }
    public Task<ClawdNet.Core.Models.ConversationSession?> GetAsync(string id, CancellationToken cancellationToken) => Task.FromResult<ClawdNet.Core.Models.ConversationSession?>(null);
    public Task<IReadOnlyList<ClawdNet.Core.Models.ConversationSession>> ListAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ClawdNet.Core.Models.ConversationSession>>(new List<ClawdNet.Core.Models.ConversationSession>());
    public Task SaveAsync(ClawdNet.Core.Models.ConversationSession session, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task<ClawdNet.Core.Models.ConversationSession?> GetMostRecentAsync(CancellationToken cancellationToken) => Task.FromResult<ClawdNet.Core.Models.ConversationSession?>(null);
    public Task<IReadOnlyList<ClawdNet.Core.Models.ConversationSession>> SearchAsync(string query, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ClawdNet.Core.Models.ConversationSession>>(new List<ClawdNet.Core.Models.ConversationSession>());
    public Task<ClawdNet.Core.Models.ConversationSession> ForkAsync(string sessionId, string? newTitle, CancellationToken cancellationToken) => throw new System.NotImplementedException();
    public Task RenameAsync(string sessionId, string newTitle, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task UpdateTagsAsync(string sessionId, IReadOnlyList<string> tags, CancellationToken cancellationToken) => Task.CompletedTask;
}

file sealed class TestTaskStore : ClawdNet.Core.Abstractions.ITaskStore
{
    public Task CreateAsync(ClawdNet.Core.Models.TaskRecord task, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task<ClawdNet.Core.Models.TaskRecord?> GetAsync(string id, CancellationToken cancellationToken) => Task.FromResult<ClawdNet.Core.Models.TaskRecord?>(null);
    public Task<IReadOnlyList<ClawdNet.Core.Models.TaskRecord>> ListAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ClawdNet.Core.Models.TaskRecord>>(new List<ClawdNet.Core.Models.TaskRecord>());
    public Task SaveAsync(ClawdNet.Core.Models.TaskRecord task, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task AppendEventAsync(string taskId, ClawdNet.Core.Models.TaskEvent taskEvent, CancellationToken cancellationToken) => Task.CompletedTask;
}

file sealed class TestPermissionService : ClawdNet.Core.Abstractions.IPermissionService
{
    public ClawdNet.Core.Models.PermissionDecision Evaluate(ClawdNet.Core.Abstractions.ITool tool, ClawdNet.Core.Models.PermissionMode mode)
    {
        return new ClawdNet.Core.Models.PermissionDecision(ClawdNet.Core.Models.PermissionDecisionKind.Deny, "test double");
    }
}

file sealed class TestTranscriptRenderer : ClawdNet.Core.Abstractions.ITranscriptRenderer
{
    public string Render(IReadOnlyList<ClawdNet.Core.Models.ConversationMessage> messages) => "";
    public string? RenderDraft(ClawdNet.Core.Models.StreamingAssistantDraft? draft) => null;
    public string? RenderPty(ClawdNet.Core.Models.PtyManagerState? state) => null;
    public string RenderFooter(ClawdNet.Core.Models.ConversationSession session, ClawdNet.Core.Models.PermissionMode permissionMode, ClawdNet.Core.Models.PtyManagerState? ptyState = null, bool followLiveOutput = true, bool hasBufferedLiveOutput = false, string? error = null) => "";
    public string? RenderActivity(ClawdNet.Core.Models.TerminalActivityState state, string? detail = null) => null;
}
