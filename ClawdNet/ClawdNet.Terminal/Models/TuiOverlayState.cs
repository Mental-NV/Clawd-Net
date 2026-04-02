namespace ClawdNet.Terminal.Models;

public sealed record TuiOverlayState(
    TuiOverlayKind Kind,
    string Title,
    string? Summary = null,
    IReadOnlyList<TuiOverlaySection>? Sections = null,
    bool RequiresConfirmation = false,
    string? PrimaryActionLabel = null,
    string? SecondaryActionLabel = null);
