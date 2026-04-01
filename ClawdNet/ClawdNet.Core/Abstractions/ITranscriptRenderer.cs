using ClawdNet.Core.Models;

namespace ClawdNet.Core.Abstractions;

public interface ITranscriptRenderer
{
    string Render(IReadOnlyList<ConversationMessage> entries);

    string RenderStatus(ConversationSession session, string? error = null);
}
