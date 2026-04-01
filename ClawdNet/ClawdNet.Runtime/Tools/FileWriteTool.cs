using System.Text.Json.Nodes;
using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Models;
using ClawdNet.Runtime.Protocols;

namespace ClawdNet.Runtime.Tools;

public sealed class FileWriteTool : ITool
{
    private readonly ILspClient _lspClient;

    public FileWriteTool(ILspClient? lspClient = null)
    {
        _lspClient = lspClient ?? new NullLspClient();
    }

    public string Name => "file_write";

    public string Description => "Write UTF-8 text to a file.";

    public ToolCategory Category => ToolCategory.Write;

    public JsonObject InputSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["path"] = new JsonObject { ["type"] = "string" },
            ["content"] = new JsonObject { ["type"] = "string" }
        },
        ["required"] = new JsonArray("path", "content")
    };

    public async Task<ToolExecutionResult> ExecuteAsync(ToolExecutionRequest request, CancellationToken cancellationToken)
    {
        var path = request.Input?["path"]?.GetValue<string>();
        var content = request.Input?["content"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(path) || content is null)
        {
            return new ToolExecutionResult(false, string.Empty, "file_write requires 'path' and 'content'.");
        }

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(path, content, cancellationToken);
        try
        {
            await _lspClient.SyncFileAsync(path, content, cancellationToken);
            return new ToolExecutionResult(true, $"Wrote {content.Length} chars to {path}");
        }
        catch (Exception ex)
        {
            return new ToolExecutionResult(true, $"Wrote {content.Length} chars to {path}{Environment.NewLine}LSP sync failed: {ex.Message}");
        }
    }
}
