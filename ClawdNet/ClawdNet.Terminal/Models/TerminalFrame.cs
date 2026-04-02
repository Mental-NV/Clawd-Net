namespace ClawdNet.Terminal.Models;

public sealed record TerminalFrame(
    string Header,
    string TranscriptPane,
    string ContextPane,
    string ComposerPane,
    string Footer,
    string? DrawerPane = null,
    string? Overlay = null,
    bool ClearScreen = true,
    bool UseAlternateScreen = true);
