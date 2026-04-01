namespace ClawdNet.Core.Models;

public enum TerminalActivityState
{
    Idle,
    Ready,
    WaitingForModel,
    AwaitingApproval,
    ShowingHelp,
    ShowingSession,
    Cleared,
    Error,
    Exiting
}
