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
        if (!string.IsNullOrWhiteSpace(viewState.Draft))
        {
            System.Console.WriteLine(viewState.Draft);
            System.Console.WriteLine();
        }

        if (!string.IsNullOrWhiteSpace(viewState.Pty))
        {
            System.Console.WriteLine(viewState.Pty);
            System.Console.WriteLine();
        }

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

    public IDisposable RegisterInterruptHandler(Action handler)
    {
        ConsoleCancelEventHandler wrappedHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            handler();
        };

        System.Console.CancelKeyPress += wrappedHandler;
        return new InterruptRegistration(wrappedHandler);
    }

    private sealed class InterruptRegistration : IDisposable
    {
        private readonly ConsoleCancelEventHandler _handler;
        private bool _disposed;

        public InterruptRegistration(ConsoleCancelEventHandler handler)
        {
            _handler = handler;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            System.Console.CancelKeyPress -= _handler;
            _disposed = true;
        }
    }
}
