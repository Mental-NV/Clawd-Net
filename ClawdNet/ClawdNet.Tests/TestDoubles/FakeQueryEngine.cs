using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Exceptions;
using ClawdNet.Core.Models;

namespace ClawdNet.Tests.TestDoubles;

public sealed class FakeQueryEngine : IQueryEngine
{
    public List<QueryRequest> Requests { get; } = [];
    public Func<QueryRequest, IAsyncEnumerable<QueryStreamEvent>>? StreamHandler { get; set; }
    public Func<QueryRequest, CancellationToken, Task<QueryExecutionResult>>? HandlerWithCancellation { get; set; }

    public Func<QueryRequest, Task<QueryExecutionResult>> Handler { get; set; }
        = request => Task.FromResult(
            new QueryExecutionResult(
                new ConversationSession(
                    request.SessionId ?? "session",
                    "Interactive session",
                    request.Model ?? "claude-sonnet-4-5",
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow,
                    [
                        new ConversationMessage("assistant", "ok", DateTimeOffset.UtcNow)
                    ]),
                "ok",
                1));

    public Task<QueryExecutionResult> AskAsync(QueryRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        if (HandlerWithCancellation is not null)
        {
            return HandlerWithCancellation(request, cancellationToken);
        }

        return Handler(request);
    }

    public async IAsyncEnumerable<QueryStreamEvent> StreamAskAsync(
        QueryRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Requests.Add(request);
        if (StreamHandler is not null)
        {
            await foreach (var streamEvent in StreamHandler(request).WithCancellation(cancellationToken))
            {
                yield return streamEvent;
            }

            yield break;
        }

        var result = await Handler(request);
        yield return new UserTurnAcceptedEvent(result.Session);

        if (!string.IsNullOrWhiteSpace(result.AssistantText))
        {
            yield return new AssistantTextDeltaStreamEvent(result.AssistantText);
            yield return new AssistantMessageCommittedEvent(result.Session, result.AssistantText);
        }

        yield return new TurnCompletedStreamEvent(result);
    }
}
