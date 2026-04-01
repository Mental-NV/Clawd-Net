namespace ClawdNet.Core.Models;

public sealed record QueryRequest(
    string Prompt,
    string? SessionId = null,
    string? Model = null,
    int MaxTurns = 8);
