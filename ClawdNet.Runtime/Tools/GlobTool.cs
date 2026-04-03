using System.Text.Json.Nodes;
using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Models;

namespace ClawdNet.Runtime.Tools;

public sealed class GlobTool : ITool
{
    public string Name => "glob";

    public string Description => "List files under a directory using a simple search pattern.";

    public ToolCategory Category => ToolCategory.ReadOnly;

    public JsonObject InputSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["path"] = new JsonObject { ["type"] = "string" },
            ["pattern"] = new JsonObject { ["type"] = "string" }
        }
    };

    public Task<ToolExecutionResult> ExecuteAsync(ToolExecutionRequest request, CancellationToken cancellationToken)
    {
        var root = request.Input?["path"]?.GetValue<string>() ?? ".";
        var pattern = request.Input?["pattern"]?.GetValue<string>() ?? "*";
        if (!Directory.Exists(root))
        {
            return Task.FromResult(new ToolExecutionResult(false, string.Empty, $"Directory '{root}' was not found."));
        }

        var files = Directory.GetFiles(root, pattern, SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        return Task.FromResult(new ToolExecutionResult(true, string.Join(Environment.NewLine, files)));
    }
}
