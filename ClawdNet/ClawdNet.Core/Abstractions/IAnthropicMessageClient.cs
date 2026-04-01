using ClawdNet.Core.Models;

namespace ClawdNet.Core.Abstractions;

public interface IAnthropicMessageClient
{
    Task<ModelResponse> SendAsync(ModelRequest request, CancellationToken cancellationToken);

    IAsyncEnumerable<ModelStreamEvent> StreamAsync(ModelRequest request, CancellationToken cancellationToken);
}
