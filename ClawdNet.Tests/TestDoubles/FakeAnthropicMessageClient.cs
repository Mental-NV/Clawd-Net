using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Models;

namespace ClawdNet.Tests.TestDoubles;

public sealed class FakeAnthropicMessageClient : IAnthropicMessageClient
{
    private readonly Queue<ModelResponse> _responses;
    private readonly Queue<IReadOnlyList<ModelStreamEvent>> _streamResponses = [];

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

    public async IAsyncEnumerable<ModelStreamEvent> StreamAsync(
        ModelRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Requests.Add(request);

        if (_streamResponses.Count > 0)
        {
            foreach (var streamEvent in _streamResponses.Dequeue())
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return streamEvent;
                await Task.Yield();
            }

            yield break;
        }

        if (_responses.Count == 0)
        {
            throw new InvalidOperationException("No fake responses remain.");
        }

        var response = _responses.Dequeue();
        yield return new MessageStartedEvent(response.Model);

        foreach (var block in response.ContentBlocks)
        {
            switch (block)
            {
                case TextContentBlock textBlock:
                    yield return new TextDeltaEvent(textBlock.Text);
                    yield return new TextCompletedEvent(textBlock.Text);
                    break;
                case ToolUseContentBlock toolBlock:
                    yield return new ToolUseStartedEvent(toolBlock.Id, toolBlock.Name);
                    yield return new ToolUseCompletedEvent(toolBlock.Id, toolBlock.Name, toolBlock.Input);
                    break;
            }
        }

        yield return new MessageCompletedEvent(response.StopReason);
    }

    public void EnqueueStream(params ModelStreamEvent[] events)
    {
        _streamResponses.Enqueue(events);
    }
}
