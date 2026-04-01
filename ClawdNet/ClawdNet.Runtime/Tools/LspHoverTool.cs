using System.Text.Json.Nodes;
using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Models;

namespace ClawdNet.Runtime.Tools;

public sealed class LspHoverTool : ITool
{
    private readonly ILspClient _lspClient;

    public LspHoverTool(ILspClient lspClient)
    {
        _lspClient = lspClient;
    }

    public string Name => "lsp_hover";
    public string Description => "Get hover text for a symbol using the configured language server.";
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

            var hover = await _lspClient.GetHoverAsync(path!, line, character, cancellationToken);
            return new ToolExecutionResult(true, string.IsNullOrWhiteSpace(hover) ? "Hover: none" : hover);
        }
        catch (Exception ex)
        {
            return new ToolExecutionResult(false, string.Empty, ex.Message);
        }
    }
}
