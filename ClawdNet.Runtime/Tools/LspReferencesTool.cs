using System.Text;
using System.Text.Json.Nodes;
using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Models;

namespace ClawdNet.Runtime.Tools;

public sealed class LspReferencesTool : ITool
{
    private readonly ILspClient _lspClient;

    public LspReferencesTool(ILspClient lspClient)
    {
        _lspClient = lspClient;
    }

    public string Name => "lsp_references";
    public string Description => "Find symbol references using the configured language server.";
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

            var locations = await _lspClient.GetReferencesAsync(path!, line, character, cancellationToken);
            if (locations.Count == 0)
            {
                return new ToolExecutionResult(true, "References: none");
            }

            var builder = new StringBuilder();
            builder.AppendLine("References:");
            foreach (var location in locations)
            {
                builder.AppendLine($"{location.Path}:{location.Line + 1}:{location.Character + 1}");
            }

            return new ToolExecutionResult(true, builder.ToString().TrimEnd());
        }
        catch (Exception ex)
        {
            return new ToolExecutionResult(false, string.Empty, ex.Message);
        }
    }
}
