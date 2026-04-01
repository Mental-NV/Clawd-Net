using System.Text.Json.Nodes;
using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Models;

namespace ClawdNet.Runtime.Tools;

public sealed class FileReadTool : ITool
{
    public string Name => "file_read";

    public string Description => "Read a UTF-8 text file from disk.";

    public JsonObject InputSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["path"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Absolute or relative filesystem path."
            }
        },
        ["required"] = new JsonArray("path")
    };

    public async Task<ToolExecutionResult> ExecuteAsync(ToolExecutionRequest request, CancellationToken cancellationToken)
    {
        var path = request.Input?["path"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(path))
        {
            return new ToolExecutionResult(false, string.Empty, "file_read requires a 'path' string.");
        }

        if (!File.Exists(path))
        {
            return new ToolExecutionResult(false, string.Empty, $"File '{path}' was not found.");
        }

        var text = await File.ReadAllTextAsync(path, cancellationToken);
        return new ToolExecutionResult(true, text);
    }
}
