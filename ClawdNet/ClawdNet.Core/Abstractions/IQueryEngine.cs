using ClawdNet.Core.Models;

namespace ClawdNet.Core.Abstractions;

public interface IQueryEngine
{
    Task<QueryExecutionResult> AskAsync(QueryRequest request, CancellationToken cancellationToken);

    IAsyncEnumerable<QueryStreamEvent> StreamAskAsync(QueryRequest request, CancellationToken cancellationToken);
}
