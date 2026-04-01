using ClawdNet.Core.Models;

namespace ClawdNet.Core.Abstractions;

public interface ITranscriptRenderer
{
    string Render(IReadOnlyList<ConversationMessage> entries);

    string? RenderDraft(StreamingAssistantDraft? draft);

    string RenderFooter(
        ConversationSession session,
        PermissionMode permissionMode,
        string? error = null);

    string? RenderActivity(TerminalActivityState state, string? detail = null);
}
