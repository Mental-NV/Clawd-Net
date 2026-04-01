using ClawdNet.Terminal.Abstractions;
using ClawdNet.Terminal.Models;

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

    public List<TerminalViewState> RenderedViews { get; } = [];

    public List<string> ErrorLines { get; } = [];

    public int ClearCount { get; private set; }

    public Action? InterruptHandler { get; private set; }

    public int ReadLineDelayMs { get; set; }

    public async Task<string?> ReadLineAsync(string prompt, CancellationToken cancellationToken)
    {
        Prompts.Add(prompt);
        if (ReadLineDelayMs > 0)
        {
            await Task.Delay(ReadLineDelayMs, cancellationToken);
        }

        return _inputs.Count > 0 ? _inputs.Dequeue() : null;
    }

    public Task<bool> ConfirmAsync(string prompt, CancellationToken cancellationToken)
    {
        Prompts.Add(prompt);
        return Task.FromResult(_confirmations.Count > 0 && _confirmations.Dequeue());
    }

    public void Render(TerminalViewState viewState)
    {
        RenderedViews.Add(viewState);
    }

    public void ClearVisible()
    {
        ClearCount++;
    }

    public void WriteErrorLine(string text)
    {
        ErrorLines.Add(text);
    }

    public IDisposable RegisterInterruptHandler(Action handler)
    {
        InterruptHandler = handler;
        return new InterruptRegistration(() => InterruptHandler = null);
    }

    public void TriggerInterrupt()
    {
        InterruptHandler?.Invoke();
    }

    private sealed class InterruptRegistration : IDisposable
    {
        private readonly Action _onDispose;
        private bool _disposed;

        public InterruptRegistration(Action onDispose)
        {
            _onDispose = onDispose;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _onDispose();
            _disposed = true;
        }
    }
}
