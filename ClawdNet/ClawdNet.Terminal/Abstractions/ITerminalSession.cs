namespace ClawdNet.Terminal.Abstractions;

public interface ITerminalSession
{
    Task<string?> ReadLineAsync(string prompt, CancellationToken cancellationToken);

    void WriteLine(string text);

    void WriteErrorLine(string text);

    void WriteStatus(string text);
}
