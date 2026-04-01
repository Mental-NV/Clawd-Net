using ClawdNet.Terminal.Abstractions;
using ClawdNet.Terminal.Models;

namespace ClawdNet.Terminal.Console;

public sealed class ConsoleTerminalSession : ITerminalSession
{
    public Task<string?> ReadLineAsync(string prompt, CancellationToken cancellationToken)
    {
        System.Console.Write(prompt);
        return Task.FromResult(System.Console.ReadLine());
    }

    public async Task<bool> ConfirmAsync(string prompt, CancellationToken cancellationToken)
    {
        var response = await ReadLineAsync($"{prompt} [y/N] ", cancellationToken);
        return string.Equals(response?.Trim(), "y", StringComparison.OrdinalIgnoreCase)
            || string.Equals(response?.Trim(), "yes", StringComparison.OrdinalIgnoreCase);
    }

    public void Render(TerminalViewState viewState)
    {
        if (viewState.ClearScreen)
        {
            ClearVisible();
        }

        System.Console.WriteLine(viewState.Header);
        System.Console.WriteLine();
        System.Console.WriteLine(viewState.Transcript);
        System.Console.WriteLine();
        if (!string.IsNullOrWhiteSpace(viewState.Activity))
        {
            System.Console.WriteLine(viewState.Activity);
            System.Console.WriteLine();
        }

        System.Console.WriteLine(viewState.Footer);
    }

    public void ClearVisible()
    {
        if (!System.Console.IsOutputRedirected)
        {
            System.Console.Clear();
            return;
        }

        System.Console.Write("\u001b[2J\u001b[H");
    }

    public void WriteErrorLine(string text)
    {
        System.Console.Error.WriteLine(text);
    }
}
