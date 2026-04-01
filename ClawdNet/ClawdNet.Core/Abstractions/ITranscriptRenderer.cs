using ClawdNet.Core.Models;

namespace ClawdNet.Core.Abstractions;

public interface ITranscriptRenderer
{
    string Render(IReadOnlyList<ConversationMessage> entries);

    string? RenderDraft(StreamingAssistantDraft? draft);

    string? RenderPty(PtySessionState? state);

    string RenderFooter(
        ConversationSession session,
        PermissionMode permissionMode,
        PtySessionState? ptyState = null,
        bool followLiveOutput = true,
        bool hasBufferedLiveOutput = false,
        string? error = null);

    string? RenderActivity(TerminalActivityState state, string? detail = null);
}
