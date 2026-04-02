namespace ClawdNet.Terminal.Models;

public sealed record PromptInputResult(
    PromptInputKind Kind,
    string? Text = null)
{
    public static PromptInputResult Submit(string text) => new(PromptInputKind.Submit, text);

    public static PromptInputResult EndOfStream() => new(PromptInputKind.EndOfStream);

    public static PromptInputResult BufferChanged(string text) => new(PromptInputKind.BufferChanged, text);

    public static PromptInputResult InsertLineBreak(string text) => new(PromptInputKind.InsertLineBreak, text);

    public static PromptInputResult HistoryPrevious() => new(PromptInputKind.HistoryPrevious);

    public static PromptInputResult HistoryNext() => new(PromptInputKind.HistoryNext);

    public static PromptInputResult ScrollPageUp() => new(PromptInputKind.ScrollPageUp);

    public static PromptInputResult ScrollPageDown() => new(PromptInputKind.ScrollPageDown);

    public static PromptInputResult ScrollBottom() => new(PromptInputKind.ScrollBottom);

    public static PromptInputResult FocusNext() => new(PromptInputKind.FocusNext);

    public static PromptInputResult FocusPrevious() => new(PromptInputKind.FocusPrevious);

    public static PromptInputResult ToggleHelp() => new(PromptInputKind.ToggleHelp);

    public static PromptInputResult ToggleSession() => new(PromptInputKind.ToggleSession);

    public static PromptInputResult TogglePty() => new(PromptInputKind.TogglePty);

    public static PromptInputResult ToggleTasks() => new(PromptInputKind.ToggleTasks);

    public static PromptInputResult ToggleActivity() => new(PromptInputKind.ToggleActivity);

    public static PromptInputResult DrawerNextItem() => new(PromptInputKind.DrawerNextItem);

    public static PromptInputResult DrawerPreviousItem() => new(PromptInputKind.DrawerPreviousItem);

    public static PromptInputResult DrawerOpenSelected() => new(PromptInputKind.DrawerOpenSelected);

    public static PromptInputResult DismissSurface() => new(PromptInputKind.DismissSurface);
}
