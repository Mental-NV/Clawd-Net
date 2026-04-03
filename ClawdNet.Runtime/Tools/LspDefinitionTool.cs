using System.Text;
using System.Text.Json.Nodes;
using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Models;

namespace ClawdNet.Runtime.Tools;

public sealed class LspDefinitionTool : ITool
{
    private readonly ILspClient _lspClient;

    public LspDefinitionTool(ILspClient lspClient)
    {
        _lspClient = lspClient;
    }

    public string Name => "lsp_definition";
    public string Description => "Find symbol definitions using the configured language server.";
    public ToolCategory Category => ToolCategory.ReadOnly;
    public JsonObject InputSchema => LspToolSchemas.PositionSchema();

    public async Task<ToolExecutionResult> ExecuteAsync(ToolExecutionRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var (path, line, character, error) = LspToolSchemas.ParsePosition(request.Input);
            if (error is not null)
            {
                return new ToolExecutionResult(false, string.Empty, error);
            }

            var locations = await _lspClient.GetDefinitionsAsync(path!, line, character, cancellationToken);
            return new ToolExecutionResult(true, FormatLocations("Definitions", locations));
        }
        catch (Exception ex)
        {
            return new ToolExecutionResult(false, string.Empty, ex.Message);
        }
    }

    private static string FormatLocations(string title, IReadOnlyList<LspLocation> locations)
    {
        if (locations.Count == 0)
        {
            return $"{title}: none";
        }

        var builder = new StringBuilder();
        builder.AppendLine($"{title}:");
        foreach (var location in locations)
        {
            builder.AppendLine($"{location.Path}:{location.Line + 1}:{location.Character + 1}");
        }

        return builder.ToString().TrimEnd();
    }
}
