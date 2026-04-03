namespace ClawdNet.Terminal.Models;

/// <summary>
/// Represents the state of a full-screen PTY overlay.
/// </summary>
public sealed record PtyFullScreenState(
    string SessionId,
    string Command,
    bool IsRunning,
    string RecentOutput,
    string StatusLine = "");
