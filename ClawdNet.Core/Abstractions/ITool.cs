using System.Text.Json.Nodes;
using ClawdNet.Core.Models;

namespace ClawdNet.Core.Abstractions;

public interface ITool
{
    string Name { get; }

    string Description { get; }

    ToolCategory Category { get; }

    bool RequiresEditReview => false;

    JsonObject InputSchema { get; }

    Task<ToolExecutionResult> ExecuteAsync(ToolExecutionRequest request, CancellationToken cancellationToken);
}
