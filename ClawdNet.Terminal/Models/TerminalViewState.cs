namespace ClawdNet.Terminal.Models;

public sealed record TerminalViewState(
    string Header,
    string Transcript,
    string Footer,
    string PromptBuffer = "",
    TerminalViewportState? Viewport = null,
    string? Draft = null,
    string? Pty = null,
    string? Activity = null,
    bool ClearScreen = true);
