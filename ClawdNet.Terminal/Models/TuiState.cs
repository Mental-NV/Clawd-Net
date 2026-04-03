using ClawdNet.Core.Models;

namespace ClawdNet.Terminal.Models;

public sealed record TuiState(
    ConversationSession Session,
    PermissionMode PermissionMode,
    IReadOnlyList<ConversationMessage> VisibleTranscript,
    IReadOnlyList<TaskRecord> RecentTasks,
    IReadOnlyList<string> ActivityFeed,
    string ComposerBuffer,
    TuiFocusTarget Focus,
    TuiDrawerState? Drawer,
    TuiOverlayState? Overlay,
    TuiLayoutState Layout,
    TerminalViewportState TranscriptViewport,
    TerminalViewportState ContextViewport,
    StreamingAssistantDraft? Draft = null,
    PtyManagerState? Pty = null,
    TerminalActivityState ActivityState = TerminalActivityState.Ready,
    string? ActivityDetail = null,
    string? Error = null,
    bool ClearScreen = true,
    bool UseAlternateScreen = true);
