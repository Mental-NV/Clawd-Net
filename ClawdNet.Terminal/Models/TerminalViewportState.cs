namespace ClawdNet.Terminal.Models;

public sealed record TerminalViewportState(
    int ScrollOffsetFromBottom = 0,
    int PageSize = 12,
    bool FollowLiveOutput = true,
    bool HasBufferedLiveOutput = false);
