using System.Text.Json;
using System.Text.Json.Nodes;
using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Commands;
using ClawdNet.Core.Models;
using ClawdNet.Core.Serialization;
using ClawdNet.Runtime.FeatureGates;
using ClawdNet.Tests.TestDoubles;
using TaskStatus = ClawdNet.Core.Models.TaskStatus;

namespace ClawdNet.Tests;

public sealed class NdjsonSerializerTests
{
    [Fact]
    public void AssistantTextDelta_serializes_to_assistant_type()
    {
        var evt = new AssistantTextDeltaStreamEvent("Hello");
        var json = NdjsonSerializer.Serialize(evt);

        Assert.NotNull(json);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.Equal("assistant", doc.GetProperty("type").GetString());
        Assert.Equal("Hello", doc.GetProperty("delta").GetString());
    }

    [Fact]
    public void AssistantMessageCommitted_serializes_with_committed_flag()
    {
        var session = CreateTestSession();
        var evt = new AssistantMessageCommittedEvent(session, "Full response");
        var json = NdjsonSerializer.Serialize(evt);

        Assert.NotNull(json);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.Equal("assistant", doc.GetProperty("type").GetString());
        Assert.True(doc.GetProperty("committed").GetBoolean());
        Assert.Equal("Full response", doc.GetProperty("message").GetString());
    }

    [Fact]
    public void ToolCallRequested_serializes_to_system_subtype()
    {
        var toolCall = new ToolCall("tc-1", "echo", JsonNode.Parse("{\"text\": \"hello\"}"));
        var evt = new ToolCallRequestedEvent(toolCall);
        var json = NdjsonSerializer.Serialize(evt);

        Assert.NotNull(json);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.Equal("system", doc.GetProperty("type").GetString());
        Assert.Equal("tool_call_requested", doc.GetProperty("subtype").GetString());
        Assert.Equal("echo", doc.GetProperty("tool").GetString());
        Assert.Equal("tc-1", doc.GetProperty("toolUseId").GetString());
    }

    [Fact]
    public void TurnCompleted_serializes_to_result_success()
    {
        var session = CreateTestSession();
        var result = new QueryExecutionResult(session, "Final text", 1);
        var evt = new TurnCompletedStreamEvent(result);
        var json = NdjsonSerializer.Serialize(evt);

        Assert.NotNull(json);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.Equal("result", doc.GetProperty("type").GetString());
        Assert.Equal("success", doc.GetProperty("subtype").GetString());
        Assert.Equal(1, doc.GetProperty("turnsExecuted").GetInt32());
        Assert.Equal("Final text", doc.GetProperty("assistantText").GetString());
    }

    [Fact]
    public void TurnFailed_serializes_to_result_error()
    {
        var evt = new TurnFailedStreamEvent("Something went wrong");
        var json = NdjsonSerializer.Serialize(evt);

        Assert.NotNull(json);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.Equal("result", doc.GetProperty("type").GetString());
        Assert.Equal("error", doc.GetProperty("subtype").GetString());
        Assert.Equal("Something went wrong", doc.GetProperty("message").GetString());
    }

    [Fact]
    public void UserTurnAccepted_serializes_to_system_subtype()
    {
        var session = CreateTestSession();
        var evt = new UserTurnAcceptedEvent(session);
        var json = NdjsonSerializer.Serialize(evt);

        Assert.NotNull(json);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.Equal("system", doc.GetProperty("type").GetString());
        Assert.Equal("user_turn_accepted", doc.GetProperty("subtype").GetString());
    }

    [Fact]
    public void PermissionDecision_serializes_with_decision_kind()
    {
        var toolCall = new ToolCall("tc-1", "shell", null);
        var decision = new PermissionDecision(PermissionDecisionKind.Ask, "Requires approval");
        var evt = new PermissionDecisionStreamEvent(toolCall, decision);
        var json = NdjsonSerializer.Serialize(evt);

        Assert.NotNull(json);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.Equal("system", doc.GetProperty("type").GetString());
        Assert.Equal("permission_decision", doc.GetProperty("subtype").GetString());
        Assert.Equal("ask", doc.GetProperty("decision").GetString());
        // approved is null when not explicitly set
        Assert.True(doc.GetProperty("approved").ValueKind == JsonValueKind.Null);
    }

    [Fact]
    public void TaskStarted_serializes_with_task_id()
    {
        var task = new TaskRecord("task-1", TaskKind.Worker, "Test task", "Do something", "session-1", "worker-session-1", "claude-sonnet-4-5", PermissionMode.Default, TaskStatus.Pending, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var evt = new TaskStartedStreamEvent(task);
        var json = NdjsonSerializer.Serialize(evt);

        Assert.NotNull(json);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.Equal("system", doc.GetProperty("type").GetString());
        Assert.Equal("task_started", doc.GetProperty("subtype").GetString());
        Assert.Equal("task-1", doc.GetProperty("taskId").GetString());
    }

    [Fact]
    public void PluginHookRecorded_returns_null_not_emitted()
    {
        var session = CreateTestSession();
        var pluginDef = new PluginDefinition("test-plugin", "test", "/path/to/test", true, null, []);
        var hookDef = new PluginHookDefinition(PluginHookKind.BeforeQuery, "echo", [], new Dictionary<string, string>(), PluginExecutionMode.Subprocess, true, false);
        var hookResult = new PluginHookResult(pluginDef, hookDef, true, "ok", false);
        var evt = new PluginHookRecordedEvent(session, hookResult);
        var json = NdjsonSerializer.Serialize(evt);

        // Plugin hook events are not emitted in the minimum viable stream-json mode
        Assert.Null(json);
    }

    private static ConversationSession CreateTestSession()
    {
        return new ConversationSession(
            "session-123",
            "Test Session",
            "claude-sonnet-4-5",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            [],
            "anthropic");
    }
}

public sealed class AskCommandHandlerOutputFormatTests
{
    [Fact]
    public async Task Output_format_stream_json_without_prompt_returns_failure()
    {
        var handler = new AskCommandHandler();
        var result = await handler.ExecuteAsync(
            CreateContext(),
            new CommandRequest(["ask", "--output-format", "stream-json"]),
            CancellationToken.None);

        // Exit code 3 because ConversationStoreException is thrown for missing prompt
        Assert.True(result.ExitCode == 1 || result.ExitCode == 3);
        Assert.Contains("prompt", result.StdErr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Input_format_stream_json_without_output_format_returns_failure()
    {
        var handler = new AskCommandHandler();
        var result = await handler.ExecuteAsync(
            CreateContext(),
            new CommandRequest(["ask", "--input-format", "stream-json", "hello"]),
            CancellationToken.None);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("--input-format=stream-json requires --output-format=stream-json", result.StdErr);
    }

    [Fact]
    public async Task Input_format_stream_json_with_output_format_text_returns_failure()
    {
        var handler = new AskCommandHandler();
        var result = await handler.ExecuteAsync(
            CreateContext(),
            new CommandRequest(["ask", "--input-format", "stream-json", "--output-format", "text", "hello"]),
            CancellationToken.None);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("--input-format=stream-json requires --output-format=stream-json", result.StdErr);
    }

    [Fact]
    public async Task Output_format_text_is_default()
    {
        var handler = new AskCommandHandler();
        var context = CreateContextWithFakeQueryEngine();
        var result = await handler.ExecuteAsync(
            context,
            new CommandRequest(["ask", "hello"]),
            CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        // Text output contains "Session:" and "Model:"
        Assert.Contains("Session:", result.StdOut);
    }

    [Fact]
    public async Task Output_format_json_returns_single_object()
    {
        var handler = new AskCommandHandler();
        var context = CreateContextWithFakeQueryEngine();
        var result = await handler.ExecuteAsync(
            context,
            new CommandRequest(["ask", "--output-format", "json", "hello"]),
            CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        var doc = JsonSerializer.Deserialize<JsonElement>(result.StdOut);
        Assert.True(doc.TryGetProperty("sessionId", out _));
        Assert.True(doc.TryGetProperty("assistantText", out _));
    }

    [Fact]
    public async Task Json_flag_is_equivalent_to_output_format_json()
    {
        var handler = new AskCommandHandler();
        var context = CreateContextWithFakeQueryEngine();
        var result = await handler.ExecuteAsync(
            context,
            new CommandRequest(["ask", "--json", "hello"]),
            CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        var doc = JsonSerializer.Deserialize<JsonElement>(result.StdOut);
        Assert.True(doc.TryGetProperty("sessionId", out _));
    }

    [Fact]
    public async Task Unknown_output_format_returns_failure()
    {
        var handler = new AskCommandHandler();
        var result = await handler.ExecuteAsync(
            CreateContext(),
            new CommandRequest(["ask", "--output-format", "xml", "hello"]),
            CancellationToken.None);

        Assert.Equal(3, result.ExitCode);
        Assert.Contains("Unknown output format", result.StdErr);
    }

    [Fact]
    public async Task Unknown_input_format_returns_failure()
    {
        var handler = new AskCommandHandler();
        var result = await handler.ExecuteAsync(
            CreateContext(),
            new CommandRequest(["ask", "--input-format", "xml", "hello"]),
            CancellationToken.None);

        Assert.Equal(3, result.ExitCode);
        Assert.Contains("Unknown input format", result.StdErr);
    }

    private static CommandContext CreateContext()
    {
        return new CommandContext(
            new DictionaryFeatureGate(),
            new TestToolRegistry2(),
            new TestToolExecutor2(),
            new TestConversationStore2(),
            new TestTaskStore2(),
            new FakeTaskManager(),
            new FakeQueryEngine(),
            new FakeProviderCatalog(),
            new FakeMcpClient(),
            new FakeLspClient(),
            new FakePluginCatalog(),
            new FakePluginRuntime(),
            new FakePlatformLauncher(),
            new TestPermissionService2(),
            new TestTranscriptRenderer2(),
            "1.0.0");
    }

    private static CommandContext CreateContextWithFakeQueryEngine()
    {
        return new CommandContext(
            new DictionaryFeatureGate(),
            new TestToolRegistry2(),
            new TestToolExecutor2(),
            new TestConversationStore2(),
            new TestTaskStore2(),
            new FakeTaskManager(),
            new StreamingFakeQueryEngine(),
            new FakeProviderCatalog(),
            new FakeMcpClient(),
            new FakeLspClient(),
            new FakePluginCatalog(),
            new FakePluginRuntime(),
            new FakePlatformLauncher(),
            new TestPermissionService2(),
            new TestTranscriptRenderer2(),
            "1.0.0");
    }
}

// Test doubles for output format tests
file sealed class TestToolRegistry2 : ClawdNet.Core.Abstractions.IToolRegistry
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

file sealed class TestToolExecutor2 : ClawdNet.Core.Abstractions.IToolExecutor
{
    public Task<ClawdNet.Core.Models.ToolExecutionResult> ExecuteAsync(ClawdNet.Core.Models.ToolExecutionRequest request, CancellationToken cancellationToken)
    {
        return Task.FromResult(new ClawdNet.Core.Models.ToolExecutionResult(true, "executed"));
    }
}

file sealed class TestConversationStore2 : ClawdNet.Core.Abstractions.IConversationStore
{
    public Task<ClawdNet.Core.Models.ConversationSession> CreateAsync(string? title, string model, CancellationToken cancellationToken, string? provider = null)
    {
        return Task.FromResult(new ClawdNet.Core.Models.ConversationSession(
            Guid.NewGuid().ToString("N"), title ?? "", model, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, [], provider ?? "anthropic"));
    }
    public Task<ClawdNet.Core.Models.ConversationSession?> GetAsync(string id, CancellationToken cancellationToken) => Task.FromResult<ClawdNet.Core.Models.ConversationSession?>(null);
    public Task<IReadOnlyList<ClawdNet.Core.Models.ConversationSession>> ListAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ClawdNet.Core.Models.ConversationSession>>(new List<ClawdNet.Core.Models.ConversationSession>());
    public Task SaveAsync(ClawdNet.Core.Models.ConversationSession session, CancellationToken cancellationToken) => Task.CompletedTask;
}

file sealed class TestTaskStore2 : ClawdNet.Core.Abstractions.ITaskStore
{
    public Task CreateAsync(ClawdNet.Core.Models.TaskRecord task, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task<ClawdNet.Core.Models.TaskRecord?> GetAsync(string id, CancellationToken cancellationToken) => Task.FromResult<ClawdNet.Core.Models.TaskRecord?>(null);
    public Task<IReadOnlyList<ClawdNet.Core.Models.TaskRecord>> ListAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ClawdNet.Core.Models.TaskRecord>>(new List<ClawdNet.Core.Models.TaskRecord>());
    public Task SaveAsync(ClawdNet.Core.Models.TaskRecord task, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task AppendEventAsync(string taskId, ClawdNet.Core.Models.TaskEvent taskEvent, CancellationToken cancellationToken) => Task.CompletedTask;
}

file sealed class TestPermissionService2 : ClawdNet.Core.Abstractions.IPermissionService
{
    public ClawdNet.Core.Models.PermissionDecision Evaluate(ClawdNet.Core.Abstractions.ITool tool, ClawdNet.Core.Models.PermissionMode mode)
    {
        return new ClawdNet.Core.Models.PermissionDecision(ClawdNet.Core.Models.PermissionDecisionKind.Deny, "test double");
    }
}

file sealed class TestTranscriptRenderer2 : ClawdNet.Core.Abstractions.ITranscriptRenderer
{
    public string Render(IReadOnlyList<ClawdNet.Core.Models.ConversationMessage> messages) => "";
    public string? RenderDraft(ClawdNet.Core.Models.StreamingAssistantDraft? draft) => null;
    public string? RenderPty(ClawdNet.Core.Models.PtyManagerState? state) => null;
    public string RenderFooter(ClawdNet.Core.Models.ConversationSession session, ClawdNet.Core.Models.PermissionMode permissionMode, ClawdNet.Core.Models.PtyManagerState? ptyState = null, bool followLiveOutput = true, bool hasBufferedLiveOutput = false, string? error = null) => "";
    public string? RenderActivity(ClawdNet.Core.Models.TerminalActivityState state, string? detail = null) => null;
}

/// <summary>
/// A fake query engine that supports both AskAsync and StreamAskAsync for testing.
/// </summary>
file sealed class StreamingFakeQueryEngine : ClawdNet.Core.Abstractions.IQueryEngine
{
    public Task<ClawdNet.Core.Models.QueryExecutionResult> AskAsync(ClawdNet.Core.Models.QueryRequest request, CancellationToken cancellationToken)
    {
        var session = new ClawdNet.Core.Models.ConversationSession(
            "test-session",
            "Test",
            request.Model ?? "claude-sonnet-4-5",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            [],
            request.Provider ?? "anthropic");
        return Task.FromResult(new ClawdNet.Core.Models.QueryExecutionResult(session, "Test response", 1));
    }

    public async IAsyncEnumerable<ClawdNet.Core.Models.QueryStreamEvent> StreamAskAsync(ClawdNet.Core.Models.QueryRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var session = new ClawdNet.Core.Models.ConversationSession(
            "test-session",
            "Test",
            request.Model ?? "claude-sonnet-4-5",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            [],
            request.Provider ?? "anthropic");

        yield return new ClawdNet.Core.Models.UserTurnAcceptedEvent(session);
        yield return new ClawdNet.Core.Models.AssistantTextDeltaStreamEvent("Test");
        yield return new ClawdNet.Core.Models.AssistantTextDeltaStreamEvent(" response");
        yield return new ClawdNet.Core.Models.AssistantMessageCommittedEvent(session, "Test response");
        yield return new ClawdNet.Core.Models.TurnCompletedStreamEvent(
            new ClawdNet.Core.Models.QueryExecutionResult(session, "Test response", 1));

        await Task.CompletedTask;
    }
}
