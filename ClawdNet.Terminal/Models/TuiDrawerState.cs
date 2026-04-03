namespace ClawdNet.Terminal.Models;

public sealed record TuiDrawerState(
    TuiDrawerKind Kind,
    string Title,
    IReadOnlyList<TuiDrawerItem> Items,
    string? DetailTitle = null,
    IReadOnlyList<string>? DetailLines = null);
