using ClawdNet.Core.Models;

namespace ClawdNet.Core.Abstractions;

/// <summary>
/// Manages persistent storage and retrieval of PTY session transcripts.
/// </summary>
public interface IPtyTranscriptStore
{
    /// <summary>
    /// Appends a single output chunk to the session transcript.
    /// This is fire-and-forget and should not block the PTY session.
    /// </summary>
    Task AppendAsync(string sessionId, PtyTranscriptChunk chunk, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads recent transcript chunks for a session. If tailCount is null, returns all available chunks.
    /// Otherwise returns the most recent tailCount chunks.
    /// </summary>
    Task<IReadOnlyList<PtyTranscriptChunk>> ReadAsync(string sessionId, int? tailCount = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns true if a transcript file exists for the given session ID.
    /// </summary>
    Task<bool> ExistsAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the transcript file for the given session ID.
    /// </summary>
    Task DeleteAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all session IDs that have transcripts.
    /// </summary>
    Task<IReadOnlyList<string>> ListSessionIdsAsync(CancellationToken cancellationToken = default);
}
