using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Models;

namespace ClawdNet.Core.Commands;

/// <summary>
/// Shows usage statistics across sessions and tasks.
/// </summary>
public sealed class StatsCommandHandler : ICommandHandler
{
    public string Name => "stats";

    public string HelpSummary => "Show usage statistics";

    public string HelpText => """
Usage: clawdnet stats [options]

Show usage statistics for sessions and tasks.

Options:
  --all             Show aggregate statistics across all sessions (default)
  --session <id>    Show statistics for a specific session

Examples:
  clawdnet stats
  clawdnet stats --session <session-id>
""";

    public bool CanHandle(CommandRequest request)
    {
        return request.Arguments.Count > 0
            && string.Equals(request.Arguments[0], "stats", StringComparison.OrdinalIgnoreCase);
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
                "Usage Statistics",
                "================",
                string.Empty,
            };

            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                // Session-specific stats
                var session = await context.ConversationStore.GetAsync(sessionId, cancellationToken);
                if (session is null)
                {
                    return CommandExecutionResult.Failure($"Session '{sessionId}' not found.", 3);
                }

                lines.AddRange(FormatSessionStats(session));
            }
            else
            {
                // Aggregate stats
                var sessions = await context.ConversationStore.ListAsync(cancellationToken);
                lines.AddRange(FormatAggregateStats(sessions));
            }

            return CommandExecutionResult.Success(string.Join(Environment.NewLine, lines));
        }
        catch (Exception ex)
        {
            return CommandExecutionResult.Failure($"Stats command failed: {ex.Message}", 1);
        }
    }

    private static List<string> FormatAggregateStats(IReadOnlyList<ConversationSession> sessions)
    {
        var lines = new List<string>
        {
            $"  Total sessions:    {sessions.Count}",
        };

        if (sessions.Count == 0)
        {
            lines.Add(string.Empty);
            lines.Add("No sessions found. Create one with: clawdnet session new \"Title\"");
            return lines;
        }

        var totalMessages = sessions.Sum(s => s.Messages.Count);
        var avgMessages = sessions.Count > 0 ? (double)totalMessages / sessions.Count : 0;

        lines.Add($"  Total messages:    {totalMessages}");
        lines.Add($"  Avg messages/sess: {avgMessages:F1}");

        // Provider distribution
        var providerGroups = sessions
            .GroupBy(s => s.Provider ?? "default (anthropic)")
            .OrderByDescending(g => g.Count())
            .ToList();

        if (providerGroups.Count > 1)
        {
            lines.Add(string.Empty);
            lines.Add("  Provider distribution:");
            foreach (var group in providerGroups)
            {
                lines.Add($"    {group.Key,-24} {group.Count()} session(s)");
            }
        }

        // Tag distribution
        var taggedSessions = sessions.Where(s => s.EffectiveTags.Count > 0).ToList();
        if (taggedSessions.Count > 0)
        {
            var allTags = taggedSessions.SelectMany(s => s.EffectiveTags)
                .GroupBy(t => t)
                .OrderByDescending(g => g.Count())
                .ToList();

            lines.Add(string.Empty);
            lines.Add("  Tags:");
            foreach (var tag in allTags)
            {
                lines.Add($"    {tag.Key,-24} {tag.Count()} session(s)");
            }
        }

        lines.Add(string.Empty);
        return lines;
    }

    private static List<string> FormatSessionStats(ConversationSession session)
    {
        var lines = new List<string>
        {
            $"  Session:         {session.Title}",
            $"  ID:              {session.Id}",
            $"  Provider:        {session.Provider ?? "default (anthropic)"}",
            $"  Model:           {session.Model}",
            $"  Messages:        {session.Messages.Count}",
            $"  Created:         {session.CreatedAtUtc.LocalDateTime}",
            $"  Updated:         {session.UpdatedAtUtc.LocalDateTime}",
        };

        if (session.EffectiveTags.Count > 0)
        {
            lines.Add($"  Tags:            {string.Join(", ", session.EffectiveTags)}");
        }

        // Message role breakdown
        var roleCounts = session.Messages
            .GroupBy(m => m.Role)
            .ToDictionary(g => g.Key, g => g.Count());

        lines.Add(string.Empty);
        lines.Add("  Message breakdown:");
        foreach (var (role, count) in roleCounts.OrderBy(kv => kv.Key))
        {
            lines.Add($"    {role,-16} {count}");
        }

        lines.Add(string.Empty);
        return lines;
    }
}
