namespace ClawdNet.Terminal.Models;

public sealed record TerminalViewState(
    string Header,
    string Transcript,
    string Footer,
    string? Activity = null,
    bool ClearScreen = true);
