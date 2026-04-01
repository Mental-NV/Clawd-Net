namespace ClawdNet.Core.Abstractions;

public sealed record ProcessRequest(string FileName, string Arguments, string? WorkingDirectory = null);
