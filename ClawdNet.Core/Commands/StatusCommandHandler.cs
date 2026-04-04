using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Models;

namespace ClawdNet.Core.Commands;

/// <summary>
/// Shows current session status and configuration.
/// </summary>
public sealed class StatusCommandHandler : ICommandHandler
{
    public string Name => "status";

    public string HelpSummary => "Show current session status and configuration";

    public string HelpText => """
Usage: clawdnet status [options]

Show the current session status and configuration.

Options:
  --session <id>    Show status for a specific session (default: most recent)

Examples:
  clawdnet status
  clawdnet status --session <session-id>
""";

    public bool CanHandle(CommandRequest request)
    {
        return request.Arguments.Count > 0
            && string.Equals(request.Arguments[0], "status", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<CommandExecutionResult> ExecuteAsync(
        CommandContext context,
        CommandRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            // Parse --session flag
            var sessionId = request.Arguments
                .Skip(1)
                .Where((arg, i) => request.Arguments.ElementAtOrDefault(i - 1) == "--session")
                .FirstOrDefault();

            ConversationSession? session = null;

            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                session = await context.ConversationStore.GetAsync(sessionId, cancellationToken);
                if (session is null)
                {
                    return CommandExecutionResult.Failure($"Session '{sessionId}' not found.", 3);
                }
            }
            else
            {
                session = await context.ConversationStore.GetMostRecentAsync(cancellationToken);
            }

            if (session is null)
            {
                return CommandExecutionResult.Success(
                    "No active session.\n\n" +
                    "Start a new session:\n" +
                    "  clawdnet                              Launch interactive TUI\n" +
                    "  clawdnet \"your prompt\"               Quick headless query\n" +
                    "  clawdnet ask \"your prompt\"           Detailed query control\n" +
                    "  clawdnet session new \"Title\"         Create a named session");
            }

            var lines = new List<string>
            {
                "Session Status",
                "=============",
                string.Empty,
                $"  Session:        {session.Title}",
                $"  ID:             {session.Id}",
                $"  Provider:       {session.Provider ?? "default (anthropic)"}",
                $"  Model:          {session.Model}",
                $"  Created:        {session.CreatedAtUtc.LocalDateTime}",
                $"  Updated:        {session.UpdatedAtUtc.LocalDateTime}",
                $"  Messages:       {session.Messages.Count}",
            };

            if (session.EffectiveTags.Count > 0)
            {
                lines.Add($"  Tags:           {string.Join(", ", session.EffectiveTags)}");
            }

            lines.AddRange(
            [
                string.Empty,
                "Permission Mode: default",
                "Tool Filters:   none active",
            ]);

            return CommandExecutionResult.Success(string.Join(Environment.NewLine, lines));
        }
        catch (Exception ex)
        {
            return CommandExecutionResult.Failure($"Status command failed: {ex.Message}", 1);
        }
    }
}
