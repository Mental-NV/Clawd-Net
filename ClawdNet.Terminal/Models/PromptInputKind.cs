namespace ClawdNet.Terminal.Models;

public enum PromptInputKind
{
    Submit,
    EndOfStream,
    BufferChanged,
    InsertLineBreak,
    HistoryPrevious,
    HistoryNext,
    ScrollPageUp,
    ScrollPageDown,
    ScrollBottom,
    FocusNext,
    FocusPrevious,
    ToggleHelp,
    ToggleSession,
    TogglePty,
    ToggleTasks,
    ToggleActivity,
    DrawerNextItem,
    DrawerPreviousItem,
    DrawerOpenSelected,
    DismissSurface
}
