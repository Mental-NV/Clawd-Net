using System.Text.Json.Nodes;
using ClawdNet.Core.Models;

namespace ClawdNet.Core.Abstractions;

public interface ITool
{
    string Name { get; }

    string Description { get; }

    JsonObject InputSchema { get; }

    Task<ToolExecutionResult> ExecuteAsync(ToolExecutionRequest request, CancellationToken cancellationToken);
}
