using System.Text.Json.Nodes;
using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Models;

namespace ClawdNet.Runtime.Tools;

public sealed class EchoTool : ITool
{
    public string Name => "echo";

    public string Description => "Echo text back to the caller.";

    public JsonObject InputSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["text"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Text to echo."
            }
        },
        ["required"] = new JsonArray("text")
    };

    public Task<ToolExecutionResult> ExecuteAsync(ToolExecutionRequest request, CancellationToken cancellationToken)
    {
        var text = request.Input?["text"]?.GetValue<string>() ?? request.RawInput ?? string.Empty;
        return Task.FromResult(new ToolExecutionResult(true, text));
    }
}
