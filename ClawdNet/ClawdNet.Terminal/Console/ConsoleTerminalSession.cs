using ClawdNet.Terminal.Abstractions;

namespace ClawdNet.Terminal.Console;

public sealed class ConsoleTerminalSession : ITerminalSession
{
    public Task<string?> ReadLineAsync(string prompt, CancellationToken cancellationToken)
    {
        System.Console.Write(prompt);
        return Task.FromResult(System.Console.ReadLine());
    }

    public void WriteLine(string text)
    {
        System.Console.WriteLine(text);
    }

    public void WriteErrorLine(string text)
    {
        System.Console.Error.WriteLine(text);
    }

    public void WriteStatus(string text)
    {
        System.Console.WriteLine(text);
    }
}
