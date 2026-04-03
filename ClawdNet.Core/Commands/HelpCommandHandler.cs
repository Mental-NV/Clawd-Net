using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Models;

namespace ClawdNet.Core.Commands;

public sealed class HelpCommandHandler : ICommandHandler
{
    private readonly IReadOnlyList<ICommandHandler> _handlers;

    public HelpCommandHandler(IEnumerable<ICommandHandler> handlers)
    {
        _handlers = handlers.ToArray();
    }

    public string Name => "help";

    public string HelpSummary => "Display help information";

    public string HelpText => """
Usage: clawdnet --help
       clawdnet -h
       clawdnet <command> --help

Display help information for ClawdNet or a specific command.

Examples:
  clawdnet --help
  clawdnet ask --help
  clawdnet provider --help
""";

    public bool CanHandle(CommandRequest request)
    {
        // Handle root --help/-h
        if (request.HasFlag("--help") || request.HasFlag("-h"))
        {
            return true;
        }

        // Handle <command> --help
        if (request.Arguments.Count >= 2
            && (request.Arguments.Contains("--help", StringComparer.Ordinal)
                || request.Arguments.Contains("-h", StringComparer.Ordinal)))
        {
            return true;
        }

        return false;
    }

    public Task<CommandExecutionResult> ExecuteAsync(
        CommandContext context,
        CommandRequest request,
        CancellationToken cancellationToken)
    {
        // Check if a specific command is requested
        if (request.Arguments.Count >= 2)
        {
            var commandName = request.Arguments[0];
            var handler = _handlers.FirstOrDefault(h =>
                string.Equals(h.Name, commandName, StringComparison.OrdinalIgnoreCase));
            if (handler is not null)
            {
                return Task.FromResult(CommandExecutionResult.Success(handler.HelpText.TrimEnd()));
            }
        }

        // Root help
        return Task.FromResult(CommandExecutionResult.Success(BuildRootHelp()));
    }

    private string BuildRootHelp()
    {
        var lines = new List<string>
        {
            "ClawdNet - .NET CLI for AI-assisted development",
            "",
            "Usage: clawdnet [options]",
            "       clawdnet [options] <prompt>",
            "       clawdnet <command> [subcommand] [options]",
            "",
            "Interactive Mode:",
            "  Running without arguments launches the full-screen TUI.",
            "",
            "Headless Mode:",
            "  -p, --print <prompt>    Send a prompt in headless print mode",
            "  ask <prompt>            Send a prompt in headless mode (detailed control)",
            "",
            "Commands:",
        };

        foreach (var handler in _handlers.OrderBy(h => h.Name))
        {
            lines.Add($"  {handler.Name,-16} {handler.HelpSummary}");
        }

        lines.AddRange(
        [
            "",
            "Common Flags:",
            "  --help, -h          Show this help message",
            "  --version, -v, -V   Show version",
            "",
            "Examples:",
            "  clawdnet                                Launch interactive TUI",
            "  clawdnet \"Summarize this project\"       Quick headless query",
            "  clawdnet -p \"What is 2+2?\"              Headless print mode",
            "  clawdnet ask --json \"Explain this code\" Structured JSON output",
            "  clawdnet provider list                  List configured providers",
            "  clawdnet session list                   List conversation sessions",
            "",
            "Use 'clawdnet <command> --help' for more information about a command.",
        ]);

        return string.Join(Environment.NewLine, lines);
    }
}
