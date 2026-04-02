namespace ClawdNet.Core.Models;

public sealed record PlatformConfiguration(
    string? EditorCommand = null,
    IReadOnlyList<string>? EditorArguments = null,
    string? BrowserCommand = null,
    IReadOnlyList<string>? BrowserArguments = null,
    string? RevealCommand = null,
    IReadOnlyList<string>? RevealArguments = null);
