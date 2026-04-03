namespace ClawdNet.Core.Models;

public sealed record LspServerDefinition(
    string Name,
    string Command,
    IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string> Environment,
    IReadOnlyList<string> FileExtensions,
    string? LanguageId = null,
    bool Enabled = true);
