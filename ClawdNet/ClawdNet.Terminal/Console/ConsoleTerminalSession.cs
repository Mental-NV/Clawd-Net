using ClawdNet.Terminal.Abstractions;
using ClawdNet.Terminal.Models;

namespace ClawdNet.Terminal.Console;

public sealed class ConsoleTerminalSession : ITerminalSession
{
    public Task<PromptInputResult> ReadPromptAsync(string prompt, string currentBuffer, CancellationToken cancellationToken)
    {
        if (System.Console.IsInputRedirected)
        {
            return ReadRedirectedPromptAsync(cancellationToken);
        }

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = System.Console.ReadKey(intercept: true);
            switch (key.Key)
            {
                case ConsoleKey.Enter:
                    return Task.FromResult(PromptInputResult.Submit(currentBuffer));
                case ConsoleKey.Backspace:
                    var updated = currentBuffer.Length > 0 ? currentBuffer[..^1] : currentBuffer;
                    return Task.FromResult(PromptInputResult.BufferChanged(updated));
                case ConsoleKey.UpArrow:
                    return Task.FromResult(PromptInputResult.HistoryPrevious());
                case ConsoleKey.DownArrow:
                    return Task.FromResult(PromptInputResult.HistoryNext());
                case ConsoleKey.PageUp:
                    return Task.FromResult(PromptInputResult.ScrollPageUp());
                case ConsoleKey.PageDown:
                    return Task.FromResult(PromptInputResult.ScrollPageDown());
                case ConsoleKey.End:
                    return Task.FromResult(PromptInputResult.ScrollBottom());
                default:
                    if (!char.IsControl(key.KeyChar))
                    {
                        return Task.FromResult(PromptInputResult.BufferChanged(currentBuffer + key.KeyChar));
                    }
                    break;
            }
        }
    }

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
        System.Console.WriteLine();
        System.Console.Write($"{(string.IsNullOrWhiteSpace(viewState.PromptBuffer) ? "> " : $"> {viewState.PromptBuffer}")}");
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

    private static async Task<PromptInputResult> ReadRedirectedPromptAsync(CancellationToken cancellationToken)
    {
        var line = await Task.Run(System.Console.ReadLine, cancellationToken);
        return line is null
            ? PromptInputResult.EndOfStream()
            : PromptInputResult.Submit(line);
    }
}
