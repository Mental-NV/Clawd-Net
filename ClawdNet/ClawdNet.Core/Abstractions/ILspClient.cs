namespace ClawdNet.Core.Abstractions;

public interface ILspClient
{
    Task<bool> PingAsync(CancellationToken cancellationToken);
}
