namespace ClawdNet.Core.Abstractions;

public interface IPtySession : IAsyncDisposable
{
    Task WriteAsync(string text, CancellationToken cancellationToken);
}
