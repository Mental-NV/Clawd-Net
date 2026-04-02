using ClawdNet.Terminal.Models;

namespace ClawdNet.Terminal.Abstractions;

public interface ITuiRenderer
{
    TerminalFrame Render(TuiState state);
}
