using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Models;

namespace ClawdNet.Runtime.Processes;

public sealed class PtyManager : IPtyManager
{
    private readonly SemaphoreSlim _sync = new(1, 1);
    private readonly Dictionary<string, IPtySession> _sessions = new(StringComparer.Ordinal);
    private readonly IPtyTranscriptStore _transcriptStore;
    private string? _currentSessionId;

    public PtyManager(IPtyTranscriptStore transcriptStore)
    {
        _transcriptStore = transcriptStore ?? throw new ArgumentNullException(nameof(transcriptStore));
    }

    public PtyManagerState State => BuildState();

    public event Action<PtyManagerState>? StateChanged;

    public async Task<PtySessionState> StartAsync(string command, string? workingDirectory, CancellationToken cancellationToken)
    {
        await _sync.WaitAsync(cancellationToken);
        try
        {
            var session = await SystemPtySession.StartAsync(command, workingDirectory, _transcriptStore, cancellationToken);
            _sessions[session.Snapshot.SessionId] = session;
            _currentSessionId = session.Snapshot.SessionId;
            session.StateChanged += HandleSessionStateChanged;
            NotifyStateChanged();
            return session.Snapshot;
        }
        finally
        {
            _sync.Release();
        }
    }

    public async Task<IReadOnlyList<PtySessionSummary>> ListAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _sync.WaitAsync(cancellationToken);
        try
        {
            return BuildState().Sessions;
        }
        finally
        {
            _sync.Release();
        }
    }

    public async Task<PtySessionState> FocusAsync(string sessionId, CancellationToken cancellationToken)
    {
        await _sync.WaitAsync(cancellationToken);
        try
        {
            var session = GetRequiredSession(sessionId);
            _currentSessionId = session.Snapshot.SessionId;
            NotifyStateChanged();
            return session.Snapshot;
        }
        finally
        {
            _sync.Release();
        }
    }

    public async Task<PtySessionState> WriteAsync(string text, string? sessionId, CancellationToken cancellationToken)
    {
        var session = await GetTargetSessionAsync(sessionId, cancellationToken);
        await session.WriteAsync(text, cancellationToken);
        return session.Snapshot;
    }

    public async Task<PtySessionState?> ReadAsync(string? sessionId, CancellationToken cancellationToken)
    {
        var session = await TryGetTargetSessionAsync(sessionId, cancellationToken);
        return session?.Snapshot;
    }

    public async Task<int> PruneExitedAsync(CancellationToken cancellationToken)
    {
        await _sync.WaitAsync(cancellationToken);
        try
        {
            var exited = _sessions.Values
                .Where(session => !session.Snapshot.IsRunning)
                .ToArray();
            foreach (var session in exited)
            {
                session.StateChanged -= HandleSessionStateChanged;
                _sessions.Remove(session.Snapshot.SessionId);
                await session.DisposeAsync();
            }

            if (_currentSessionId is not null && !_sessions.ContainsKey(_currentSessionId))
            {
                PromoteCurrentSession(_currentSessionId);
            }

            if (exited.Length > 0)
            {
                NotifyStateChanged();
            }

            return exited.Length;
        }
        finally
        {
            _sync.Release();
        }
    }

    public async Task<IReadOnlyList<PtyTranscriptChunk>> GetTranscriptAsync(string sessionId, int? tailCount = null, CancellationToken cancellationToken = default)
    {
        await _sync.WaitAsync(cancellationToken);
        try
        {
            if (_sessions.TryGetValue(sessionId, out var session))
            {
                return await session.GetTranscriptAsync(tailCount, cancellationToken);
            }
            // Session not found - return empty list rather than throwing
            return Array.Empty<PtyTranscriptChunk>();
        }
        finally
        {
            _sync.Release();
        }
    }

    public async Task<PtySessionState?> CloseAsync(string? sessionId, CancellationToken cancellationToken)
    {
        var session = await TryGetTargetSessionAsync(sessionId, cancellationToken);
        if (session is null)
        {
            return null;
        }

        await session.CloseAsync(cancellationToken);
        await _sync.WaitAsync(cancellationToken);
        try
        {
            if (string.Equals(_currentSessionId, session.Snapshot.SessionId, StringComparison.Ordinal))
            {
                PromoteCurrentSession(session.Snapshot.SessionId);
            }

            NotifyStateChanged();
        }
        finally
        {
            _sync.Release();
        }

        return session.Snapshot;
    }

    public async ValueTask DisposeAsync()
    {
        await _sync.WaitAsync();
        try
        {
            foreach (var session in _sessions.Values)
            {
                session.StateChanged -= HandleSessionStateChanged;
                await session.DisposeAsync();
            }

            _sessions.Clear();
            _currentSessionId = null;
        }
        finally
        {
            _sync.Release();
            _sync.Dispose();
        }
    }

    private void HandleSessionStateChanged(PtySessionState state)
    {
        if (!state.IsRunning && string.Equals(_currentSessionId, state.SessionId, StringComparison.Ordinal))
        {
            _sync.Wait();
            try
            {
                PromoteCurrentSession(state.SessionId);
            }
            finally
            {
                _sync.Release();
            }
        }

        NotifyStateChanged();
    }

    private async Task<IPtySession> GetTargetSessionAsync(string? sessionId, CancellationToken cancellationToken)
    {
        var session = await TryGetTargetSessionAsync(sessionId, cancellationToken);
        return session ?? throw new InvalidOperationException("No active PTY session.");
    }

    private async Task<IPtySession?> TryGetTargetSessionAsync(string? sessionId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _sync.WaitAsync(cancellationToken);
        try
        {
            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                return _sessions.GetValueOrDefault(sessionId);
            }

            return _currentSessionId is null ? null : _sessions.GetValueOrDefault(_currentSessionId);
        }
        finally
        {
            _sync.Release();
        }
    }

    private IPtySession GetRequiredSession(string sessionId)
    {
        return _sessions.GetValueOrDefault(sessionId)
            ?? throw new InvalidOperationException($"PTY session '{sessionId}' was not found.");
    }

    private PtyManagerState BuildState()
    {
        var summaries = _sessions.Values
            .Select(session => session.Snapshot)
            .OrderByDescending(snapshot => snapshot.UpdatedAtUtc)
            .Select(snapshot => new PtySessionSummary(
                snapshot.SessionId,
                snapshot.Command,
                snapshot.WorkingDirectory,
                snapshot.StartedAtUtc,
                snapshot.UpdatedAtUtc,
                snapshot.IsRunning,
                snapshot.ExitCode,
                string.Equals(snapshot.SessionId, _currentSessionId, StringComparison.Ordinal),
                snapshot.IsOutputClipped))
            .ToArray();
        var current = _currentSessionId is null ? null : _sessions.GetValueOrDefault(_currentSessionId)?.Snapshot;
        return new PtyManagerState(_currentSessionId, current, summaries);
    }

    private void PromoteCurrentSession(string closingOrExitedSessionId)
    {
        var replacement = _sessions.Values
            .Select(session => session.Snapshot)
            .Where(snapshot => !string.Equals(snapshot.SessionId, closingOrExitedSessionId, StringComparison.Ordinal) && snapshot.IsRunning)
            .OrderByDescending(snapshot => snapshot.UpdatedAtUtc)
            .FirstOrDefault();

        if (replacement is not null)
        {
            _currentSessionId = replacement.SessionId;
            return;
        }

        var latestRemaining = _sessions.Values
            .Select(session => session.Snapshot)
            .Where(snapshot => !string.Equals(snapshot.SessionId, closingOrExitedSessionId, StringComparison.Ordinal))
            .OrderByDescending(snapshot => snapshot.UpdatedAtUtc)
            .FirstOrDefault();

        _currentSessionId = latestRemaining?.SessionId;
    }

    private void NotifyStateChanged()
    {
        StateChanged?.Invoke(BuildState());
    }
}
