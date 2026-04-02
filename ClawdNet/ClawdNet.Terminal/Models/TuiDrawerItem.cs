namespace ClawdNet.Terminal.Models;

public sealed record TuiDrawerItem(
    string Id,
    string Title,
    string? Subtitle = null,
    bool IsActive = false,
    bool IsSelected = false);
