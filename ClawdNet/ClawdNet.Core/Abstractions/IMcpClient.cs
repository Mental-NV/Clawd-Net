namespace ClawdNet.Core.Abstractions;

public interface IMcpClient
{
    Task<bool> PingAsync(CancellationToken cancellationToken);
}
