using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Models;

namespace ClawdNet.Tests.TestDoubles;

public sealed class FakePtyManager : IPtyManager
{
    private readonly Dictionary<string, PtySessionState> _sessions = new(StringComparer.Ordinal);
    private string? _currentSessionId;

    public PtyManagerState State => BuildState();

    public event Action<PtyManagerState>? StateChanged;

    public List<(string Command, string? WorkingDirectory)> Starts { get; } = [];

    public List<(string? SessionId, string Text)> Writes { get; } = [];

    public List<string?> Focuses { get; } = [];

    public int CloseCount { get; private set; }

    public Func<string, string?, PtySessionState>? StartHandler { get; set; }

    public Func<string, string?, PtySessionState>? WriteHandler { get; set; }

    public Task<PtySessionState> StartAsync(string command, string? workingDirectory, CancellationToken cancellationToken, TimeSpan? timeout = null, bool isBackground = false)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Starts.Add((command, workingDirectory));
        var state = StartHandler?.Invoke(command, workingDirectory)
            ?? NewState(command, workingDirectory ?? Environment.CurrentDirectory, string.Empty, true, null, false, timeout: timeout, isBackground: isBackground);
        _sessions[state.SessionId] = state;
        _currentSessionId = state.SessionId;
        NotifyChanged();
        return Task.FromResult(state);
    }

    public Task<IReadOnlyList<PtySessionSummary>> ListAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<PtySessionSummary>>(BuildState().Sessions);
    }

    public Task<PtySessionState> FocusAsync(string sessionId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_sessions.TryGetValue(sessionId, out var state))
        {
            throw new InvalidOperationException($"PTY session '{sessionId}' was not found.");
        }

        Focuses.Add(sessionId);
        _currentSessionId = sessionId;
        NotifyChanged();
        return Task.FromResult(state);
    }

    public Task<PtySessionState> WriteAsync(string text, string? sessionId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var targetId = ResolveSessionId(sessionId) ?? throw new InvalidOperationException("No active PTY session.");
        Writes.Add((targetId, text));
        var current = _sessions[targetId];
        var updated = WriteHandler?.Invoke(targetId, text)
            ?? current with
            {
                RecentOutput = current.RecentOutput + text,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
        _sessions[targetId] = updated;
        _currentSessionId = targetId;
        NotifyChanged();
        return Task.FromResult(updated);
    }

    public Task<PtySessionState?> CloseAsync(string? sessionId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var targetId = ResolveSessionId(sessionId);
        if (targetId is null)
        {
            return Task.FromResult<PtySessionState?>(null);
        }

        CloseCount++;
        var current = _sessions[targetId];
        var updated = current with
        {
            IsRunning = false,
            ExitCode = 0,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        _sessions[targetId] = updated;
        if (string.Equals(_currentSessionId, targetId, StringComparison.Ordinal))
        {
            _currentSessionId = _sessions.Values
                .Where(state => state.IsRunning && !string.Equals(state.SessionId, targetId, StringComparison.Ordinal))
                .OrderByDescending(state => state.UpdatedAtUtc)
                .Select(state => state.SessionId)
                .FirstOrDefault()
                ?? _sessions.Values
                    .Where(state => !string.Equals(state.SessionId, targetId, StringComparison.Ordinal))
                    .OrderByDescending(state => state.UpdatedAtUtc)
                    .Select(state => state.SessionId)
                    .FirstOrDefault();
        }

        NotifyChanged();
        return Task.FromResult<PtySessionState?>(updated);
    }

    public Task<PtySessionState?> ReadAsync(string? sessionId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var targetId = ResolveSessionId(sessionId);
        return Task.FromResult(targetId is null ? null : _sessions[targetId]);
    }

    public Task<int> PruneExitedAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var exitedIds = _sessions.Values.Where(state => !state.IsRunning).Select(state => state.SessionId).ToArray();
        foreach (var exitedId in exitedIds)
        {
            _sessions.Remove(exitedId);
        }

        if (_currentSessionId is not null && !_sessions.ContainsKey(_currentSessionId))
        {
            _currentSessionId = _sessions.Values.OrderByDescending(state => state.UpdatedAtUtc).Select(state => state.SessionId).FirstOrDefault();
        }

        NotifyChanged();
        return Task.FromResult(exitedIds.Length);
    }

    public Task<IReadOnlyList<PtyTranscriptChunk>> GetTranscriptAsync(string sessionId, int? tailCount = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Fake implementation returns empty - tests can override if needed
        return Task.FromResult<IReadOnlyList<PtyTranscriptChunk>>(Array.Empty<PtyTranscriptChunk>());
    }

    public void Publish(PtySessionState? state, bool makeCurrent = true)
    {
        if (state is null)
        {
            _sessions.Clear();
            _currentSessionId = null;
            NotifyChanged();
            return;
        }

        _sessions[state.SessionId] = state;
        if (makeCurrent || _currentSessionId is null)
        {
            _currentSessionId = state.SessionId;
        }

        NotifyChanged();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public static PtySessionState NewState(
        string command,
        string workingDirectory,
        string recentOutput,
        bool isRunning,
        int? exitCode,
        bool isOutputClipped,
        string? sessionId = null,
        TimeSpan? timeout = null,
        bool isBackground = false,
        DateTimeOffset? completedAtUtc = null,
        int outputLineCount = 0)
    {
        var now = DateTimeOffset.UtcNow;
        return new PtySessionState(
            sessionId ?? Guid.NewGuid().ToString("N"),
            command,
            workingDirectory,
            now,
            now,
            isRunning,
            exitCode,
            recentOutput,
            isOutputClipped,
            timeout,
            isBackground,
            completedAtUtc,
            outputLineCount);
    }

    private string? ResolveSessionId(string? sessionId)
    {
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            return _sessions.ContainsKey(sessionId) ? sessionId : null;
        }

        return _currentSessionId;
    }

    private PtyManagerState BuildState()
    {
        var sessions = _sessions.Values
            .OrderByDescending(state => state.UpdatedAtUtc)
            .Select(state => new PtySessionSummary(
                state.SessionId,
                state.Command,
                state.WorkingDirectory,
                state.StartedAtUtc,
                state.UpdatedAtUtc,
                state.IsRunning,
                state.ExitCode,
                string.Equals(state.SessionId, _currentSessionId, StringComparison.Ordinal),
                state.IsOutputClipped,
                state.Timeout,
                state.IsBackground,
                state.CompletedAtUtc,
                state.OutputLineCount))
            .ToArray();
        var current = _currentSessionId is null || !_sessions.TryGetValue(_currentSessionId, out var selected) ? null : selected;
        return new PtyManagerState(_currentSessionId, current, sessions);
    }

    private void NotifyChanged()
    {
        StateChanged?.Invoke(BuildState());
    }
}
