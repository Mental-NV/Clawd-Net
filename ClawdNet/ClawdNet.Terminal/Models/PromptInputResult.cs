namespace ClawdNet.Terminal.Models;

public sealed record PromptInputResult(
    PromptInputKind Kind,
    string? Text = null)
{
    public static PromptInputResult Submit(string text) => new(PromptInputKind.Submit, text);

    public static PromptInputResult EndOfStream() => new(PromptInputKind.EndOfStream);

    public static PromptInputResult BufferChanged(string text) => new(PromptInputKind.BufferChanged, text);

    public static PromptInputResult HistoryPrevious() => new(PromptInputKind.HistoryPrevious);

    public static PromptInputResult HistoryNext() => new(PromptInputKind.HistoryNext);

    public static PromptInputResult ScrollPageUp() => new(PromptInputKind.ScrollPageUp);

    public static PromptInputResult ScrollPageDown() => new(PromptInputKind.ScrollPageDown);

    public static PromptInputResult ScrollBottom() => new(PromptInputKind.ScrollBottom);
}
