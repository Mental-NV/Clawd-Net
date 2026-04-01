namespace ClawdNet.Terminal.Models;

public enum PromptInputKind
{
    Submit,
    EndOfStream,
    BufferChanged,
    HistoryPrevious,
    HistoryNext,
    ScrollPageUp,
    ScrollPageDown,
    ScrollBottom
}
