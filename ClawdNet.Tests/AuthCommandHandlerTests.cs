using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Commands;
using ClawdNet.Core.Models;
using ClawdNet.Runtime.FeatureGates;
using ClawdNet.Tests.TestDoubles;

namespace ClawdNet.Tests;

public class AuthCommandHandlerTests
{
    [Fact]
    public async Task AuthStatus_WithEnvVars_ShowsConfiguredProviders()
    {
        Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", "test-key");
        try
        {
            var catalog = new FakeProviderCatalog();
            var handler = new AuthCommandHandler(catalog);
            var result = await handler.ExecuteAsync(
                CreateContext(),
                new CommandRequest(new[] { "auth", "status" }),
                CancellationToken.None);

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("✓ anthropic", result.StdOut);
            Assert.Contains("configured", result.StdOut);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", null);
        }
    }

    [Fact]
    public async Task AuthStatus_WithoutEnvVars_ShowsMissingProviders()
    {
        Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", null);
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", null);
        var catalog = new FakeProviderCatalog();
        var handler = new AuthCommandHandler(catalog);
        var result = await handler.ExecuteAsync(
            CreateContext(),
            new CommandRequest(new[] { "auth", "status" }),
            CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("✗ anthropic", result.StdOut);
        Assert.Contains("missing", result.StdOut);
    }

    [Fact]
    public async Task AuthLogin_WithoutBrowser_ShowsEnvVarGuidance()
    {
        Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", null);
        var catalog = new FakeProviderCatalog();
        var handler = new AuthCommandHandler(catalog);
        var result = await handler.ExecuteAsync(
            CreateContext(),
            new CommandRequest(new[] { "auth", "login" }),
            CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("ANTHROPIC_API_KEY", result.StdOut);
        Assert.Contains("--browser", result.StdOut);
    }

    [Fact]
    public async Task AuthLogout_ClearsTokens()
    {
        var mockOauth = new MockOAuthService();
        var catalog = new FakeProviderCatalog();
        var handler = new AuthCommandHandler(catalog, mockOauth);
        var result = await handler.ExecuteAsync(
            CreateContext(),
            new CommandRequest(new[] { "auth", "logout" }),
            CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.True(mockOauth.LogoutCalled);
        Assert.Contains("cleared", result.StdOut);
    }

    [Fact]
    public async Task AuthStatus_WithOAuthService_ShowsOAuthStatus()
    {
        var mockOauth = new MockOAuthService
        {
            AccountInfo = new OAuthAccountInfo
            {
                Email = "test@example.com",
                SubscriptionType = "pro"
            }
        };
        var catalog = new FakeProviderCatalog();
        var handler = new AuthCommandHandler(catalog, mockOauth);
        var result = await handler.ExecuteAsync(
            CreateContext(),
            new CommandRequest(new[] { "auth", "status" }),
            CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("OAuth token: active", result.StdOut);
        Assert.Contains("test@example.com", result.StdOut);
        Assert.Contains("pro", result.StdOut);
    }

    [Fact]
    public async Task AuthLogin_WithBrowser_ButNoOAuthService_Fails()
    {
        var catalog = new FakeProviderCatalog();
        var handler = new AuthCommandHandler(catalog);
        var result = await handler.ExecuteAsync(
            CreateContext(),
            new CommandRequest(new[] { "auth", "login", "--browser" }),
            CancellationToken.None);

        Assert.Equal(1, result.ExitCode);
        var output = result.StdOut + result.StdErr;
        Assert.Contains("not available", output);
    }

    [Fact]
    public void AuthHelp_MentionsBrowserLogin()
    {
        var catalog = new FakeProviderCatalog();
        var handler = new AuthCommandHandler(catalog);
        Assert.Contains("--browser", handler.HelpText);
        Assert.Contains("OAuth", handler.HelpText);
    }

    [Fact]
    public void AuthHelp_DoesNotFrameOAuthAsNonGoal()
    {
        var catalog = new FakeProviderCatalog();
        var handler = new AuthCommandHandler(catalog);
        // Should not contain the old "intentionally not supported" language
        Assert.DoesNotContain("intentionally not supported", handler.HelpText);
        Assert.DoesNotContain("is not currently supported", handler.HelpText);
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

    private sealed class MockOAuthService : IOAuthService
    {
        public bool IsSupported => true;
        public bool LoginCalled { get; private set; }
        public bool LogoutCalled { get; private set; }
        public OAuthAccountInfo? AccountInfo { get; set; }

        public Task<OAuthAccountInfo> LoginAsync(OAuthLoginOptions options, CancellationToken cancellationToken = default)
        {
            LoginCalled = true;
            return Task.FromResult(AccountInfo ?? new OAuthAccountInfo { Email = "test@test.com" });
        }

        public Task LogoutAsync(CancellationToken cancellationToken = default)
        {
            LogoutCalled = true;
            return Task.CompletedTask;
        }

        public Task<OAuthAccountInfo?> GetAccountInfoAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(AccountInfo);
        }

        public Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<string?>("mock-token");
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

// Test doubles needed for CommandContext (file-scoped to avoid conflicts)
file sealed class TestToolRegistry : IToolRegistry
{
    private readonly List<ITool> _tools = new();
    public IReadOnlyCollection<ITool> Tools => _tools;
    public void Register(ITool tool) => _tools.Add(tool);
    public void RegisterRange(IEnumerable<ITool> tools) => _tools.AddRange(tools);
    public void UnregisterWhere(Func<ITool, bool> predicate) => _tools.RemoveAll(t => predicate(t));
    public bool TryGet(string name, out ITool? tool)
    {
        tool = _tools.FirstOrDefault(t => t.Name == name);
        return tool is not null;
    }
}

file sealed class TestToolExecutor : IToolExecutor
{
    public Task<ToolExecutionResult> ExecuteAsync(ToolExecutionRequest request, CancellationToken cancellationToken)
    {
        return Task.FromResult(new ToolExecutionResult(true, "executed"));
    }
}

file sealed class TestConversationStore : IConversationStore
{
    public Task<ConversationSession> CreateAsync(string? title, string model, CancellationToken cancellationToken, string? provider = null)
    {
        return Task.FromResult(new ConversationSession(
            Guid.NewGuid().ToString("N"), title ?? "", model, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, [], provider ?? "anthropic"));
    }
    public Task<ConversationSession?> GetAsync(string id, CancellationToken cancellationToken) => Task.FromResult<ConversationSession?>(null);
    public Task<IReadOnlyList<ConversationSession>> ListAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ConversationSession>>(new List<ConversationSession>());
    public Task SaveAsync(ConversationSession session, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task<ConversationSession?> GetMostRecentAsync(CancellationToken cancellationToken) => Task.FromResult<ConversationSession?>(null);
    public Task<IReadOnlyList<ConversationSession>> SearchAsync(string query, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ConversationSession>>(new List<ConversationSession>());
    public Task<ConversationSession> ForkAsync(string sessionId, string? newTitle, CancellationToken cancellationToken) => throw new NotImplementedException();
    public Task RenameAsync(string sessionId, string newTitle, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task UpdateTagsAsync(string sessionId, IReadOnlyList<string> tags, CancellationToken cancellationToken) => Task.CompletedTask;
}

file sealed class TestTaskStore : ITaskStore
{
    public Task CreateAsync(TaskRecord task, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task<TaskRecord?> GetAsync(string id, CancellationToken cancellationToken) => Task.FromResult<TaskRecord?>(null);
    public Task<IReadOnlyList<TaskRecord>> ListAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<TaskRecord>>(new List<TaskRecord>());
    public Task SaveAsync(TaskRecord task, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task AppendEventAsync(string taskId, TaskEvent taskEvent, CancellationToken cancellationToken) => Task.CompletedTask;
}

file sealed class TestPermissionService : IPermissionService
{
    public PermissionDecision Evaluate(ITool tool, PermissionMode mode)
    {
        return new PermissionDecision(PermissionDecisionKind.Deny, "test double");
    }
}

file sealed class TestTranscriptRenderer : ITranscriptRenderer
{
    public string Render(IReadOnlyList<ConversationMessage> messages) => "";
    public string? RenderDraft(StreamingAssistantDraft? draft) => null;
    public string? RenderPty(PtyManagerState? state) => null;
    public string RenderFooter(ConversationSession session, PermissionMode permissionMode, PtyManagerState? ptyState = null, bool followLiveOutput = true, bool hasBufferedLiveOutput = false, string? error = null) => "";
    public string? RenderActivity(TerminalActivityState state, string? detail = null) => null;
}
