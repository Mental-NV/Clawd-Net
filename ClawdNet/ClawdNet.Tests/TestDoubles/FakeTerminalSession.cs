using ClawdNet.Terminal.Abstractions;

namespace ClawdNet.Tests.TestDoubles;

public sealed class FakeTerminalSession : ITerminalSession
{
    private readonly Queue<string?> _inputs;
    private readonly Queue<bool> _confirmations;

    public FakeTerminalSession(IEnumerable<string?> inputs, IEnumerable<bool>? confirmations = null)
    {
        _inputs = new Queue<string?>(inputs);
        _confirmations = new Queue<bool>(confirmations ?? []);
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

    public Task<bool> ConfirmAsync(string prompt, CancellationToken cancellationToken)
    {
        Prompts.Add(prompt);
        return Task.FromResult(_confirmations.Count > 0 && _confirmations.Dequeue());
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
