using ClawdNet.Core.Abstractions;

namespace ClawdNet.Runtime.Protocols;

public sealed class NullMcpClient : IMcpClient
{
    public Task<bool> PingAsync(CancellationToken cancellationToken) => Task.FromResult(false);
}
