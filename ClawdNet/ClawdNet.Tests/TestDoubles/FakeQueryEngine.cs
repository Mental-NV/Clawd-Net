using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Exceptions;
using ClawdNet.Core.Models;

namespace ClawdNet.Tests.TestDoubles;

public sealed class FakeQueryEngine : IQueryEngine
{
    public List<QueryRequest> Requests { get; } = [];

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
        return Handler(request);
    }
}
