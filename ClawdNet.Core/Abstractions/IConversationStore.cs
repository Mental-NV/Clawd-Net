using ClawdNet.Core.Models;

namespace ClawdNet.Core.Abstractions;

public interface IConversationStore
{
    Task<ConversationSession> CreateAsync(string? title, string model, CancellationToken cancellationToken, string? provider = null);

    Task<ConversationSession?> GetAsync(string sessionId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ConversationSession>> ListAsync(CancellationToken cancellationToken);

    Task SaveAsync(ConversationSession session, CancellationToken cancellationToken);
}
