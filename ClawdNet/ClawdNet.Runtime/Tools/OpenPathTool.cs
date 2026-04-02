using System.Text.Json.Nodes;
using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Models;

namespace ClawdNet.Runtime.Tools;

public sealed class OpenPathTool : ITool
{
    private readonly IPlatformLauncher _platformLauncher;

    public OpenPathTool(IPlatformLauncher platformLauncher)
    {
        _platformLauncher = platformLauncher;
    }

    public string Name => "open_path";

    public string Description => "Open a file or directory in the configured editor or OS launcher.";

    public ToolCategory Category => ToolCategory.Execute;

    public JsonObject InputSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["path"] = new JsonObject { ["type"] = "string" },
            ["line"] = new JsonObject { ["type"] = "integer" },
            ["column"] = new JsonObject { ["type"] = "integer" }
        },
        ["required"] = new JsonArray("path")
    };

    public async Task<ToolExecutionResult> ExecuteAsync(ToolExecutionRequest request, CancellationToken cancellationToken)
    {
        var path = request.Input?["path"]?.GetValue<string>()?.Trim();
        if (string.IsNullOrWhiteSpace(path))
        {
            return new ToolExecutionResult(false, string.Empty, "open_path requires a 'path' string.");
        }

        var result = await _platformLauncher.OpenPathAsync(
            new PlatformOpenRequest(
                path,
                request.Input?["line"]?.GetValue<int?>(),
                request.Input?["column"]?.GetValue<int?>()),
            cancellationToken);
        return result.Success
            ? new ToolExecutionResult(true, result.Message)
            : new ToolExecutionResult(false, string.Empty, result.Error ?? "Failed to open path.");
    }
}
