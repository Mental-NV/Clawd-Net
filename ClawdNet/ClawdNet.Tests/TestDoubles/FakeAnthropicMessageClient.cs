using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Models;

namespace ClawdNet.Tests.TestDoubles;

public sealed class FakeAnthropicMessageClient : IAnthropicMessageClient
{
    private readonly Queue<ModelResponse> _responses;

    public FakeAnthropicMessageClient(params ModelResponse[] responses)
    {
        _responses = new Queue<ModelResponse>(responses);
    }

    public List<ModelRequest> Requests { get; } = [];

    public Task<ModelResponse> SendAsync(ModelRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        if (_responses.Count == 0)
        {
            throw new InvalidOperationException("No fake responses remain.");
        }

        return Task.FromResult(_responses.Dequeue());
    }
}
