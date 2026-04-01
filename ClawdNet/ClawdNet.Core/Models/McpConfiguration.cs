namespace ClawdNet.Core.Models;

public sealed record McpConfiguration(IReadOnlyList<McpServerDefinition> Servers);
