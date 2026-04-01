namespace ClawdNet.Core.Models;

public enum TerminalActivityState
{
    Idle,
    Ready,
    WaitingForModel,
    StreamingResponse,
    RunningTool,
    ReviewingEdits,
    AwaitingApproval,
    ShowingHelp,
    ShowingSession,
    Cleared,
    Interrupted,
    Error,
    Exiting
}
