using ClawdNet.Core.Models;

namespace ClawdNet.Core.Abstractions;

public interface IConversationStore
{
    Task<ConversationSession> CreateAsync(string? title, string model, CancellationToken cancellationToken, string? provider = null);

    Task<ConversationSession?> GetAsync(string sessionId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ConversationSession>> ListAsync(CancellationToken cancellationToken);

    Task SaveAsync(ConversationSession session, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the most recently updated session, or null if none exist.
    /// Used by --continue to resume the last active session.
    /// </summary>
    Task<ConversationSession?> GetMostRecentAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Searches sessions by ID prefix or name substring match.
    /// Used by --resume [value] to find a session when the user provides a partial identifier.
    /// </summary>
    Task<IReadOnlyList<ConversationSession>> SearchAsync(string query, CancellationToken cancellationToken);
}
