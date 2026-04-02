namespace ClawdNet.Terminal.Models;

public sealed record TuiLayoutState(
    int Width,
    int Height,
    int TranscriptWidth,
    int ContextWidth,
    int DrawerWidth = 36);
