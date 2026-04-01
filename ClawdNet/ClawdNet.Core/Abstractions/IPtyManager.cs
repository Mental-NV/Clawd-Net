using ClawdNet.Core.Models;

namespace ClawdNet.Core.Abstractions;

public interface IPtyManager : IAsyncDisposable
{
    PtySessionState? CurrentState { get; }

    event Action<PtySessionState?>? SessionChanged;

    Task<PtySessionState> StartAsync(string command, string? workingDirectory, CancellationToken cancellationToken);

    Task<PtySessionState> WriteAsync(string text, CancellationToken cancellationToken);

    Task<PtySessionState?> CloseAsync(CancellationToken cancellationToken);

    Task<PtySessionState?> ReadAsync(CancellationToken cancellationToken);
}
