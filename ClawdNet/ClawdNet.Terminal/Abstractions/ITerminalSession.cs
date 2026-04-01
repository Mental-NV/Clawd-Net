using ClawdNet.Terminal.Models;

namespace ClawdNet.Terminal.Abstractions;

public interface ITerminalSession
{
    Task<string?> ReadLineAsync(string prompt, CancellationToken cancellationToken);

    Task<bool> ConfirmAsync(string prompt, CancellationToken cancellationToken);

    void Render(TerminalViewState viewState);

    void ClearVisible();

    void WriteErrorLine(string text);
}
