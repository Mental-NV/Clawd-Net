using ClawdNet.Terminal.Abstractions;

namespace ClawdNet.Tests.TestDoubles;

public sealed class FakeTerminalSession : ITerminalSession
{
    private readonly Queue<string?> _inputs;

    public FakeTerminalSession(IEnumerable<string?> inputs)
    {
        _inputs = new Queue<string?>(inputs);
    }

    public List<string> Prompts { get; } = [];

    public List<string> OutputLines { get; } = [];

    public List<string> ErrorLines { get; } = [];

    public List<string> StatusLines { get; } = [];

    public Task<string?> ReadLineAsync(string prompt, CancellationToken cancellationToken)
    {
        Prompts.Add(prompt);
        return Task.FromResult(_inputs.Count > 0 ? _inputs.Dequeue() : (string?)null);
    }

    public void WriteLine(string text)
    {
        OutputLines.Add(text);
    }

    public void WriteErrorLine(string text)
    {
        ErrorLines.Add(text);
    }

    public void WriteStatus(string text)
    {
        StatusLines.Add(text);
    }
}
