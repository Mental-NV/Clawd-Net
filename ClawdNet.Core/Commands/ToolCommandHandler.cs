using System.Text.Json.Nodes;
using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Models;

namespace ClawdNet.Core.Commands;

public sealed class ToolCommandHandler : ICommandHandler
{
    public string Name => "tool";

    public string HelpSummary => "Invoke a tool directly (debug/operator use)";

    public string HelpText => """
Usage: clawdnet tool <toolName> [args...]

Invoke a registered tool directly. This is an additive operator-debug surface
with no direct legacy CLI equivalent.

Arguments:
  toolName    Name of the tool to invoke
  args...     Arguments passed to the tool as joined text

Examples:
  clawdnet tool echo "hello world"
  clawdnet tool file_read path/to/file.txt
""";

    public bool CanHandle(CommandRequest request)
    {
        return request.Arguments.Count >= 2
            && string.Equals(request.Arguments[0], "tool", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<CommandExecutionResult> ExecuteAsync(
        CommandContext context,
        CommandRequest request,
        CancellationToken cancellationToken)
    {
        var toolName = request.Arguments[1];
        var input = request.Arguments.Count > 2
            ? string.Join(' ', request.Arguments.Skip(2))
            : string.Empty;

        var toolInput = new JsonObject { ["text"] = input };
        var result = await context.ToolExecutor.ExecuteAsync(new ToolExecutionRequest(toolName, toolInput, input), cancellationToken);

        return result.Success
            ? CommandExecutionResult.Success(result.Output)
            : CommandExecutionResult.Failure(result.Error ?? "Tool execution failed.");
    }
}
