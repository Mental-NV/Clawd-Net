using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Models;

namespace ClawdNet.Core.Commands;

public sealed class PlatformCommandHandler : ICommandHandler
{
    public string Name => "platform";

    public string HelpSummary => "Open files in editors or URLs in browsers";

    public string HelpText => """
Usage: clawdnet platform open <path> [--line N] [--column N]
       clawdnet platform browse <url>

Launch platform integration for files or URLs.

Commands:
  open <path> [options]  Open a file in the configured editor
  browse <url>           Open a URL in the default browser

Options:
  --line N      Open at line N (editor-dependent)
  --column N    Open at column N (editor-dependent)

Examples:
  clawdnet platform open src/Program.cs --line 42
  clawdnet platform browse https://example.com
""";

    public bool CanHandle(CommandRequest request)
    {
        return request.Arguments.Count >= 2
            && string.Equals(request.Arguments[0], "platform", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<CommandExecutionResult> ExecuteAsync(
        CommandContext context,
        CommandRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var action = request.Arguments[1];
            if (string.Equals(action, "browse", StringComparison.OrdinalIgnoreCase) && request.Arguments.Count >= 3)
            {
                var url = string.Join(' ', request.Arguments.Skip(2)).Trim();
                var result = await context.PlatformLauncher.OpenUrlAsync(url, cancellationToken);
                return result.Success
                    ? CommandExecutionResult.Success(result.Message)
                    : CommandExecutionResult.Failure(result.Error ?? "Failed to open URL.");
            }

            if (string.Equals(action, "open", StringComparison.OrdinalIgnoreCase) && request.Arguments.Count >= 3)
            {
                var options = ParseOpenArguments(request.Arguments.Skip(2).ToArray());
                var result = await context.PlatformLauncher.OpenPathAsync(
                    new PlatformOpenRequest(options.Path, options.Line, options.Column),
                    cancellationToken);
                return result.Success
                    ? CommandExecutionResult.Success(result.Message)
                    : CommandExecutionResult.Failure(result.Error ?? "Failed to open path.");
            }

            return CommandExecutionResult.Failure("Supported platform commands: platform open <path> [--line N] [--column N], platform browse <url>.");
        }
        catch (InvalidOperationException ex)
        {
            return CommandExecutionResult.Failure(ex.Message);
        }
    }

    private static OpenArguments ParseOpenArguments(IReadOnlyList<string> args)
    {
        int? line = null;
        int? column = null;
        var pathParts = new List<string>();

        for (var index = 0; index < args.Count; index++)
        {
            switch (args[index])
            {
                case "--line" when index + 1 < args.Count && int.TryParse(args[index + 1], out var parsedLine):
                    line = parsedLine;
                    index++;
                    break;
                case "--column" when index + 1 < args.Count && int.TryParse(args[index + 1], out var parsedColumn):
                    column = parsedColumn;
                    index++;
                    break;
                default:
                    pathParts.Add(args[index]);
                    break;
            }
        }

        var path = string.Join(' ', pathParts).Trim();
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("platform open requires a path.");
        }

        return new OpenArguments(path, line, column);
    }

    private sealed record OpenArguments(string Path, int? Line, int? Column);
}
