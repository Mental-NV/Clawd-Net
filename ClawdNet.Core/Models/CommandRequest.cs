namespace ClawdNet.Core.Models;

public sealed record CommandRequest(IReadOnlyList<string> Arguments)
{
    public bool HasFlag(string flag) => Arguments.Contains(flag, StringComparer.Ordinal);
}
