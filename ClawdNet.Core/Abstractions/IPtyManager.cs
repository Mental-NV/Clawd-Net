using ClawdNet.Core.Models;

namespace ClawdNet.Core.Abstractions;

public interface IPtyManager : IAsyncDisposable
{
    PtyManagerState State { get; }

    event Action<PtyManagerState>? StateChanged;

    Task<PtySessionState> StartAsync(string command, string? workingDirectory, CancellationToken cancellationToken, TimeSpan? timeout = null, bool isBackground = false);

    Task<IReadOnlyList<PtySessionSummary>> ListAsync(CancellationToken cancellationToken);

    Task<PtySessionState> FocusAsync(string sessionId, CancellationToken cancellationToken);

    Task<PtySessionState> WriteAsync(string text, string? sessionId, CancellationToken cancellationToken);

    Task<PtySessionState?> CloseAsync(string? sessionId, CancellationToken cancellationToken);

    Task<PtySessionState?> ReadAsync(string? sessionId, CancellationToken cancellationToken);

    Task<int> PruneExitedAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Returns recent transcript chunks for a PTY session. Returns empty list if session not found.
    /// </summary>
    Task<IReadOnlyList<PtyTranscriptChunk>> GetTranscriptAsync(string sessionId, int? tailCount = null, CancellationToken cancellationToken = default);
}
