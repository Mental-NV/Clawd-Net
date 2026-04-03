namespace ClawdNet.Core.Models;

public sealed record LspConfiguration(IReadOnlyList<LspServerDefinition> Servers);
