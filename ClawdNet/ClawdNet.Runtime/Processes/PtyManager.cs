using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Models;

namespace ClawdNet.Runtime.Processes;

public sealed class PtyManager : IPtyManager
{
    private readonly SemaphoreSlim _sync = new(1, 1);
    private IPtySession? _session;

    public PtySessionState? CurrentState => _session?.Snapshot;

    public event Action<PtySessionState?>? SessionChanged;

    public async Task<PtySessionState> StartAsync(string command, string? workingDirectory, CancellationToken cancellationToken)
    {
        await _sync.WaitAsync(cancellationToken);
        try
        {
            if (_session is not null && _session.Snapshot.IsRunning)
            {
                throw new InvalidOperationException("A PTY session is already active.");
            }

            if (_session is not null)
            {
                _session.StateChanged -= HandleSessionStateChanged;
                await _session.DisposeAsync();
                _session = null;
            }

            _session = await SystemPtySession.StartAsync(command, workingDirectory, cancellationToken);
            _session.StateChanged += HandleSessionStateChanged;
            SessionChanged?.Invoke(_session.Snapshot);
            return _session.Snapshot;
        }
        finally
        {
            _sync.Release();
        }
    }

    public async Task<PtySessionState> WriteAsync(string text, CancellationToken cancellationToken)
    {
        var session = _session ?? throw new InvalidOperationException("No active PTY session.");
        await session.WriteAsync(text, cancellationToken);
        return session.Snapshot;
    }

    public Task<PtySessionState?> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(CurrentState);
    }

    public async Task<PtySessionState?> CloseAsync(CancellationToken cancellationToken)
    {
        var session = _session;
        if (session is null)
        {
            return null;
        }

        await session.CloseAsync(cancellationToken);
        return session.Snapshot;
    }

    public async ValueTask DisposeAsync()
    {
        await _sync.WaitAsync();
        try
        {
            if (_session is not null)
            {
                _session.StateChanged -= HandleSessionStateChanged;
                await _session.DisposeAsync();
                _session = null;
            }
        }
        finally
        {
            _sync.Release();
            _sync.Dispose();
        }
    }

    private void HandleSessionStateChanged(PtySessionState state)
    {
        SessionChanged?.Invoke(state);
    }
}
