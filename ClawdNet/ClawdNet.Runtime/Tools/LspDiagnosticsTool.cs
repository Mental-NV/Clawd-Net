using System.Text;
using System.Text.Json.Nodes;
using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Models;

namespace ClawdNet.Runtime.Tools;

public sealed class LspDiagnosticsTool : ITool
{
    private readonly ILspClient _lspClient;

    public LspDiagnosticsTool(ILspClient lspClient)
    {
        _lspClient = lspClient;
    }

    public string Name => "lsp_diagnostics";
    public string Description => "Get diagnostics for a file from the configured language server.";
    public ToolCategory Category => ToolCategory.ReadOnly;
    public JsonObject InputSchema => LspToolSchemas.PathSchema();

    public async Task<ToolExecutionResult> ExecuteAsync(ToolExecutionRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var path = request.Input?["path"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(path))
            {
                return new ToolExecutionResult(false, string.Empty, "lsp_diagnostics requires 'path'.");
            }

            var diagnostics = await _lspClient.GetDiagnosticsAsync(path, cancellationToken);
            if (diagnostics.Count == 0)
            {
                return new ToolExecutionResult(true, "Diagnostics: none");
            }

            var builder = new StringBuilder();
            builder.AppendLine("Diagnostics:");
            foreach (var diagnostic in diagnostics)
            {
                builder.AppendLine($"{diagnostic.Path}:{diagnostic.Line + 1}:{diagnostic.Character + 1} [{diagnostic.Severity}] {diagnostic.Message}");
            }

            return new ToolExecutionResult(true, builder.ToString().TrimEnd());
        }
        catch (Exception ex)
        {
            return new ToolExecutionResult(false, string.Empty, ex.Message);
        }
    }
}
