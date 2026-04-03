namespace ClawdNet.Core.Models;

public sealed record FileEdit(
    string Path,
    EditOperation Operation,
    IReadOnlyList<EditHunk>? Hunks = null,
    string? Content = null);
