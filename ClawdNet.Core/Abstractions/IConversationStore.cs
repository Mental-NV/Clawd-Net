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

    /// <summary>
    /// Creates a new session by copying the message history from an existing session.
    /// The new session gets a fresh ID and optional title override.
    /// Used by --fork-session to branch from an existing conversation.
    /// </summary>
    Task<ConversationSession> ForkAsync(string sessionId, string? newTitle, CancellationToken cancellationToken);

    /// <summary>
    /// Updates the title of an existing session.
    /// </summary>
    Task RenameAsync(string sessionId, string newTitle, CancellationToken cancellationToken);

    /// <summary>
    /// Updates the tags for an existing session.
    /// </summary>
    Task UpdateTagsAsync(string sessionId, IReadOnlyList<string> tags, CancellationToken cancellationToken);
}
