using ClawdNet.Core.Abstractions;

namespace ClawdNet.Runtime.Protocols;

public sealed class NullLspClient : ILspClient
{
    public Task<bool> PingAsync(CancellationToken cancellationToken) => Task.FromResult(false);
}
