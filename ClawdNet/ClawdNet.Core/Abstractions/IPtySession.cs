using ClawdNet.Core.Models;

namespace ClawdNet.Core.Abstractions;

public interface IPtySession : IAsyncDisposable
{
    PtySessionState Snapshot { get; }

    event Action<PtySessionState>? StateChanged;

    IAsyncEnumerable<PtyOutputChunk> GetOutputAsync(CancellationToken cancellationToken);

    Task WriteAsync(string text, CancellationToken cancellationToken);

    Task CloseAsync(CancellationToken cancellationToken);

    Task TerminateAsync(CancellationToken cancellationToken);
}
