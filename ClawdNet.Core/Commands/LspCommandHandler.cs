using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Models;

namespace ClawdNet.Core.Commands;

public sealed class LspCommandHandler : ICommandHandler
{
    public string Name => "lsp";

    public string HelpSummary => "List, ping, and query LSP servers";

    public string HelpText => """
Usage: clawdnet lsp list
       clawdnet lsp ping <server>
       clawdnet lsp diagnostics <path>

Inspect configured LSP (Language Server Protocol) servers.

Commands:
  list                 List all configured LSP servers
  ping <server>        Check if an LSP server is connected
  diagnostics <path>   Get diagnostics for a file from LSP servers

Examples:
  clawdnet lsp list
  clawdnet lsp ping csharp
  clawdnet lsp diagnostics src/Program.cs
""";

    public bool CanHandle(CommandRequest request)
    {
        return request.Arguments.Count > 0
            && string.Equals(request.Arguments[0], "lsp", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<CommandExecutionResult> ExecuteAsync(
        CommandContext context,
        CommandRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Arguments.Count < 2)
        {
            return CommandExecutionResult.Failure("lsp requires a subcommand: list, ping <server>, diagnostics <path>.");
        }

        return request.Arguments[1].ToLowerInvariant() switch
        {
            "list" => await ListAsync(context.LspClient, cancellationToken),
            "ping" => await PingAsync(context.LspClient, request.Arguments.Skip(2).ToArray(), cancellationToken),
            "diagnostics" => await DiagnosticsAsync(context.LspClient, request.Arguments.Skip(2).ToArray(), cancellationToken),
            _ => CommandExecutionResult.Failure($"Unknown lsp subcommand '{request.Arguments[1]}'.")
        };
    }

    private static async Task<CommandExecutionResult> ListAsync(ILspClient lspClient, CancellationToken cancellationToken)
    {
        await lspClient.InitializeAsync(cancellationToken);
        if (lspClient.Servers.Count == 0)
        {
            return CommandExecutionResult.Success("No LSP servers configured.");
        }

        var lines = lspClient.Servers.Select(server =>
            $"{server.Name} | enabled={server.Enabled} | connected={server.Connected} | extensions={string.Join(',', server.FileExtensions)}" +
            (string.IsNullOrWhiteSpace(server.Error) ? string.Empty : $" | error={server.Error}"));
        return CommandExecutionResult.Success(string.Join(Environment.NewLine, lines));
    }

    private static async Task<CommandExecutionResult> PingAsync(ILspClient lspClient, string[] args, CancellationToken cancellationToken)
    {
        if (args.Length == 0 || string.IsNullOrWhiteSpace(args[0]))
        {
            return CommandExecutionResult.Failure("lsp ping requires a server name.");
        }

        var result = await lspClient.PingAsync(args[0], cancellationToken);
        if (result is null)
        {
            return CommandExecutionResult.Failure($"LSP server '{args[0]}' was not found.", 3);
        }

        return result.Connected
            ? CommandExecutionResult.Success($"{result.Name} is connected.")
            : CommandExecutionResult.Failure(result.Error ?? $"LSP server '{result.Name}' is unavailable.", 2);
    }

    private static async Task<CommandExecutionResult> DiagnosticsAsync(ILspClient lspClient, string[] args, CancellationToken cancellationToken)
    {
        if (args.Length == 0 || string.IsNullOrWhiteSpace(args[0]))
        {
            return CommandExecutionResult.Failure("lsp diagnostics requires a file path.");
        }

        var diagnostics = await lspClient.GetDiagnosticsAsync(args[0], cancellationToken);
        if (diagnostics.Count == 0)
        {
            return CommandExecutionResult.Success("Diagnostics: none");
        }

        var lines = diagnostics.Select(diagnostic =>
            $"{diagnostic.Path}:{diagnostic.Line + 1}:{diagnostic.Character + 1} [{diagnostic.Severity}] {diagnostic.Message}");
        return CommandExecutionResult.Success(string.Join(Environment.NewLine, lines));
    }
}
