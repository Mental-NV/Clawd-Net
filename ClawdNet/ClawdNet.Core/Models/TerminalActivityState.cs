namespace ClawdNet.Core.Models;

public enum TerminalActivityState
{
    Idle,
    Ready,
    WaitingForModel,
    StreamingResponse,
    RunningTool,
    AwaitingApproval,
    ShowingHelp,
    ShowingSession,
    Cleared,
    Interrupted,
    Error,
    Exiting
}
