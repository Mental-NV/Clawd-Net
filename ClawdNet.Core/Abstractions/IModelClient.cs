using ClawdNet.Core.Models;

namespace ClawdNet.Core.Abstractions;

public interface IModelClient
{
    Task<ModelResponse> SendAsync(ModelRequest request, CancellationToken cancellationToken);

    IAsyncEnumerable<ModelStreamEvent> StreamAsync(ModelRequest request, CancellationToken cancellationToken);
}
