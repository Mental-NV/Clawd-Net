using System.Text.Json.Nodes;
using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Models;

namespace ClawdNet.Runtime.Tools;

public sealed class OpenUrlTool : ITool
{
    private readonly IPlatformLauncher _platformLauncher;

    public OpenUrlTool(IPlatformLauncher platformLauncher)
    {
        _platformLauncher = platformLauncher;
    }

    public string Name => "open_url";

    public string Description => "Open a URL in the configured browser or OS launcher.";

    public ToolCategory Category => ToolCategory.Execute;

    public JsonObject InputSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["url"] = new JsonObject { ["type"] = "string" }
        },
        ["required"] = new JsonArray("url")
    };

    public async Task<ToolExecutionResult> ExecuteAsync(ToolExecutionRequest request, CancellationToken cancellationToken)
    {
        var url = request.Input?["url"]?.GetValue<string>()?.Trim();
        if (string.IsNullOrWhiteSpace(url))
        {
            return new ToolExecutionResult(false, string.Empty, "open_url requires a 'url' string.");
        }

        var result = await _platformLauncher.OpenUrlAsync(url, cancellationToken);
        return result.Success
            ? new ToolExecutionResult(true, result.Message)
            : new ToolExecutionResult(false, string.Empty, result.Error ?? "Failed to open URL.");
    }
}
