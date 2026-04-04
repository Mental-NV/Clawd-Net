using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Models;

namespace ClawdNet.Core.Commands;

/// <summary>
/// Shows token and cost usage information.
/// </summary>
public sealed class UsageCommandHandler : ICommandHandler
{
    public string Name => "usage";

    public string HelpSummary => "Show token and cost usage information";

    public string HelpText => """
Usage: clawdnet usage [options]

Show token and cost usage information for sessions.

Token tracking is not currently integrated into the session model.
Cost estimation requires provider pricing data which is not available.

Options:
  --all             Show aggregate usage across all sessions (default)
  --session <id>    Show usage for a specific session

Examples:
  clawdnet usage
  clawdnet usage --session <session-id>
""";

    public bool CanHandle(CommandRequest request)
    {
        return request.Arguments.Count > 0
            && string.Equals(request.Arguments[0], "usage", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<CommandExecutionResult> ExecuteAsync(
        CommandContext context,
        CommandRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            // Parse flags
            var sessionId = request.Arguments
                .Skip(1)
                .Where((arg, i) => request.Arguments.ElementAtOrDefault(i - 1) == "--session")
                .FirstOrDefault();

            var lines = new List<string>
            {
                "Token and Cost Usage",
                "====================",
                string.Empty,
            };

            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                // Session-specific usage
                var session = await context.ConversationStore.GetAsync(sessionId, cancellationToken);
                if (session is null)
                {
                    return CommandExecutionResult.Failure($"Session '{sessionId}' not found.", 3);
                }

                lines.AddRange(FormatSessionUsage(session));
            }
            else
            {
                // Aggregate usage
                var sessions = await context.ConversationStore.ListAsync(cancellationToken);
                lines.AddRange(FormatAggregateUsage(sessions));
            }

            // Add note about tracking limitations
            lines.Add(string.Empty);
            lines.Add("Note: Token counting and cost estimation are not currently tracked.");
            lines.Add("The message counts above are available from session metadata.");
            lines.Add("Accurate token usage requires integration with provider APIs.");

            return CommandExecutionResult.Success(string.Join(Environment.NewLine, lines));
        }
        catch (Exception ex)
        {
            return CommandExecutionResult.Failure($"Usage command failed: {ex.Message}", 1);
        }
    }

    private static List<string> FormatAggregateUsage(IReadOnlyList<ConversationSession> sessions)
    {
        var lines = new List<string>
        {
            $"  Total sessions:    {sessions.Count}",
        };

        if (sessions.Count == 0)
        {
            lines.Add(string.Empty);
            lines.Add("No sessions found.");
            return lines;
        }

        var totalMessages = sessions.Sum(s => s.Messages.Count);
        lines.Add($"  Total messages:    {totalMessages}");

        // Show per-session breakdown
        lines.Add(string.Empty);
        lines.Add("  Per-session message count:");

        foreach (var session in sessions.OrderByDescending(s => s.Messages.Count).Take(10))
        {
            lines.Add($"    {session.Title,-30} {session.Messages.Count,6} messages");
        }

        if (sessions.Count > 10)
        {
            lines.Add($"    ... and {sessions.Count - 10} more session(s)");
        }

        return lines;
    }

    private static List<string> FormatSessionUsage(ConversationSession session)
    {
        var lines = new List<string>
        {
            $"  Session:         {session.Title}",
            $"  ID:              {session.Id}",
            $"  Messages:        {session.Messages.Count}",
        };

        // Message role breakdown
        var roleCounts = session.Messages
            .GroupBy(m => m.Role)
            .ToDictionary(g => g.Key, g => g.Count());

        lines.Add(string.Empty);
        lines.Add("  Messages by role:");
        foreach (var (role, count) in roleCounts.OrderBy(kv => kv.Key))
        {
            lines.Add($"    {role,-16} {count}");
        }

        return lines;
    }
}
