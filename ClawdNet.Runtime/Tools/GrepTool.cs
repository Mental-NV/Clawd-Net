using System.Text;
using System.Text.Json.Nodes;
using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Models;

namespace ClawdNet.Runtime.Tools;

public sealed class GrepTool : ITool
{
    public string Name => "grep";

    public string Description => "Search file contents for a plain text pattern.";

    public ToolCategory Category => ToolCategory.ReadOnly;

    public JsonObject InputSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["path"] = new JsonObject { ["type"] = "string" },
            ["pattern"] = new JsonObject { ["type"] = "string" }
        },
        ["required"] = new JsonArray("pattern")
    };

    public async Task<ToolExecutionResult> ExecuteAsync(ToolExecutionRequest request, CancellationToken cancellationToken)
    {
        var root = request.Input?["path"]?.GetValue<string>() ?? ".";
        var pattern = request.Input?["pattern"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return new ToolExecutionResult(false, string.Empty, "grep requires a 'pattern' string.");
        }

        if (!Directory.Exists(root))
        {
            return new ToolExecutionResult(false, string.Empty, $"Directory '{root}' was not found.");
        }

        var builder = new StringBuilder();
        foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var lines = await File.ReadAllLinesAsync(file, cancellationToken);
            for (var i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    builder.Append(file).Append(':').Append(i + 1).Append(": ").AppendLine(lines[i]);
                }
            }
        }

        return new ToolExecutionResult(true, builder.ToString().TrimEnd());
    }
}
