namespace ClawdNet.Terminal.Models;

public sealed record TuiOverlaySection(
    string Title,
    IReadOnlyList<string> Lines);
