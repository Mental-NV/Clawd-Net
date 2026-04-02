using ClawdNet.Core.Models;

namespace ClawdNet.Terminal.Models;

public sealed record TuiState(
    ConversationSession Session,
    PermissionMode PermissionMode,
    IReadOnlyList<ConversationMessage> VisibleTranscript,
    IReadOnlyList<TaskRecord> RecentTasks,
    string ComposerBuffer,
    TuiFocusTarget Focus,
    TuiOverlayState? Overlay,
    TuiLayoutState Layout,
    TerminalViewportState TranscriptViewport,
    TerminalViewportState ContextViewport,
    StreamingAssistantDraft? Draft = null,
    PtySessionState? Pty = null,
    TerminalActivityState ActivityState = TerminalActivityState.Ready,
    string? ActivityDetail = null,
    string? Error = null,
    bool ClearScreen = true,
    bool UseAlternateScreen = true);
