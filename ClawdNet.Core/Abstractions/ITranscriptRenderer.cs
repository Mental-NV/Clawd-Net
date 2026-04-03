using ClawdNet.Core.Models;

namespace ClawdNet.Core.Abstractions;

public interface ITranscriptRenderer
{
    string Render(IReadOnlyList<ConversationMessage> entries);

    string? RenderDraft(StreamingAssistantDraft? draft);

    string? RenderPty(PtyManagerState? state);

    string RenderFooter(
        ConversationSession session,
        PermissionMode permissionMode,
        PtyManagerState? ptyState = null,
        bool followLiveOutput = true,
        bool hasBufferedLiveOutput = false,
        string? error = null);

    string? RenderActivity(TerminalActivityState state, string? detail = null);
}
