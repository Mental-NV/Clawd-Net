namespace ClawdNet.Terminal.Models;

public sealed record TuiOverlayState(
    TuiOverlayKind Kind,
    string Title,
    string Content,
    bool RequiresConfirmation = false);
