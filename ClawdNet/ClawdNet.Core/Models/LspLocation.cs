namespace ClawdNet.Core.Models;

public sealed record LspLocation(
    string Path,
    int Line,
    int Character);
