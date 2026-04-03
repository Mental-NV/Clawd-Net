namespace ClawdNet.Core.Models;

public sealed record LspDiagnostic(
    string Path,
    int Line,
    int Character,
    string Severity,
    string Message);
