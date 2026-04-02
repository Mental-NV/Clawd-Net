using ClawdNet.Terminal.Models;

namespace ClawdNet.Terminal.Abstractions;

public interface ITerminalSession
{
    Task<PromptInputResult> ReadPromptAsync(string prompt, string currentBuffer, CancellationToken cancellationToken);

    Task<string?> ReadLineAsync(string prompt, CancellationToken cancellationToken);

    Task<bool> ConfirmAsync(string prompt, CancellationToken cancellationToken);

    void Render(TerminalViewState viewState);

    void RenderFrame(TerminalFrame frame);

    void EnterAlternateScreen();

    void LeaveAlternateScreen();

    TerminalSize GetTerminalSize();

    void ClearVisible();

    void WriteErrorLine(string text);

    IDisposable RegisterInterruptHandler(Action handler);
}
