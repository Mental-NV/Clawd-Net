namespace ClawdNet.Core.Models;

public sealed record McpServerDefinition(
    string Name,
    string Command,
    IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string> Environment,
    bool Enabled = true,
    bool ToolsReadOnly = false);
