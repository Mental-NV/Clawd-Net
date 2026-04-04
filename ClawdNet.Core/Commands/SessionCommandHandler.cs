using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Exceptions;
using ClawdNet.Core.Models;

namespace ClawdNet.Core.Commands;

public sealed class SessionCommandHandler : ICommandHandler
{
    public string Name => "session";

    public string HelpSummary => "Create, list, inspect, rename, and tag conversation sessions";

    public string HelpText => """
Usage: clawdnet session new [title]
       clawdnet session list
       clawdnet session show <id>
       clawdnet session rename <id> <new-name>
       clawdnet session tag <id> <tag-name>
       clawdnet session fork <id> [new-title]

Manage conversation sessions.

Commands:
  new [title]        Create a new session with an optional title
  list               List all sessions
  show <id>          Show session details including recent messages and tags
  rename <id> <name> Rename an existing session
  tag <id> <tag>     Add or toggle a tag on a session
  fork <id> [title]  Fork a session into a new branch with copied history

Examples:
  clawdnet session new "Debug Session"
  clawdnet session list
  clawdnet session show abc123def
  clawdnet session rename abc123def "Renamed Session"
  clawdnet session tag abc123def work
  clawdnet session fork abc123def "Branch for testing"
""";

    public bool CanHandle(CommandRequest request)
    {
        return request.Arguments.Count >= 2
            && string.Equals(request.Arguments[0], "session", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<CommandExecutionResult> ExecuteAsync(
        CommandContext context,
        CommandRequest request,
        CancellationToken cancellationToken)
    {
        var action = request.Arguments[1];

        if (string.Equals(action, "new", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var title = request.Arguments.Count > 2
                    ? string.Join(' ', request.Arguments.Skip(2))
                    : null;
                var provider = await context.ProviderCatalog.ResolveAsync(null, cancellationToken);
                var model = string.IsNullOrWhiteSpace(provider.DefaultModel)
                    ? throw new ModelProviderConfigurationException(provider.Name, "Model must be specified because the provider has no default model configured.")
                    : provider.DefaultModel!;
                var session = await context.ConversationStore.CreateAsync(title, model, cancellationToken, provider.Name);
                var transcript = context.TranscriptRenderer.Render(session.Messages);
                var output = $"Created session {session.Id}: {session.Title}{Environment.NewLine}{transcript}";
                return CommandExecutionResult.Success(output.TrimEnd());
            }
            catch (ModelProviderConfigurationException ex)
            {
                return CommandExecutionResult.Failure(ex.Message, 2);
            }
        }

        if (string.Equals(action, "list", StringComparison.OrdinalIgnoreCase))
        {
            var sessions = await context.ConversationStore.ListAsync(cancellationToken);
            if (sessions.Count == 0)
            {
                return CommandExecutionResult.Success("No sessions found.");
            }

            var lines = sessions.Select(session => $"{session.Id} | {session.Title} | {session.UpdatedAtUtc:O} | {session.Provider}/{session.Model}");
            return CommandExecutionResult.Success(string.Join(Environment.NewLine, lines));
        }

        if (string.Equals(action, "show", StringComparison.OrdinalIgnoreCase))
        {
            if (request.Arguments.Count < 3)
            {
                return CommandExecutionResult.Failure("Session ID is required. Usage: session show <id>");
            }

            var sessionId = string.Join(' ', request.Arguments.Skip(2));
            var session = await context.ConversationStore.GetAsync(sessionId, cancellationToken);
            if (session is null)
            {
                return CommandExecutionResult.Failure($"Session '{sessionId}' not found.", 3);
            }

            var lines = new List<string>
            {
                $"ID:        {session.Id}",
                $"Title:     {session.Title}",
                $"Provider:  {session.Provider}",
                $"Model:     {session.Model}",
                $"Created:   {session.CreatedAtUtc:O}",
                $"Updated:   {session.UpdatedAtUtc:O}",
                $"Messages:  {session.Messages.Count}",
            };

            // Add tags if present
            var tags = session.EffectiveTags;
            if (tags.Count > 0)
            {
                lines.Add($"Tags:      {string.Join(", ", tags)}");
            }
            else
            {
                lines.Add("Tags:      (none)");
            }

            lines.Add(string.Empty);
            lines.Add("Recent messages:");

            // Show last 10 messages
            var recentMessages = session.Messages.TakeLast(10).ToList();
            foreach (var msg in recentMessages)
            {
                var role = msg.Role.ToUpperInvariant();
                var content = msg.Content.Length > 200 ? msg.Content[..200] + "..." : msg.Content;
                lines.Add($"  [{role}] {content}");
            }

            return CommandExecutionResult.Success(string.Join(Environment.NewLine, lines));
        }

        if (string.Equals(action, "rename", StringComparison.OrdinalIgnoreCase))
        {
            if (request.Arguments.Count < 4)
            {
                return CommandExecutionResult.Failure("Session ID and new name are required. Usage: session rename <id> <new-name>.");
            }

            var sessionId = request.Arguments[2];
            var newName = string.Join(' ', request.Arguments.Skip(3));

            try
            {
                await context.ConversationStore.RenameAsync(sessionId, newName, cancellationToken);
                return CommandExecutionResult.Success($"Session '{sessionId}' renamed to '{newName}'.");
            }
            catch (ConversationStoreException ex)
            {
                return CommandExecutionResult.Failure(ex.Message, 3);
            }
        }

        if (string.Equals(action, "tag", StringComparison.OrdinalIgnoreCase))
        {
            if (request.Arguments.Count < 4)
            {
                return CommandExecutionResult.Failure("Session ID and tag name are required. Usage: session tag <id> <tag-name>.");
            }

            var sessionId = request.Arguments[2];
            var tagName = string.Join(' ', request.Arguments.Skip(3));

            try
            {
                var session = await context.ConversationStore.GetAsync(sessionId, cancellationToken);
                if (session is null)
                {
                    return CommandExecutionResult.Failure($"Session '{sessionId}' not found.", 3);
                }

                var currentTags = session.EffectiveTags.ToList();
                // Toggle behavior: if tag exists, remove it; otherwise add it
                if (currentTags.Contains(tagName, StringComparer.OrdinalIgnoreCase))
                {
                    currentTags.RemoveAll(t => string.Equals(t, tagName, StringComparison.OrdinalIgnoreCase));
                    await context.ConversationStore.UpdateTagsAsync(sessionId, currentTags, cancellationToken);
                    return CommandExecutionResult.Success($"Tag '{tagName}' removed from session '{sessionId}'.");
                }
                else
                {
                    currentTags.Add(tagName);
                    await context.ConversationStore.UpdateTagsAsync(sessionId, currentTags, cancellationToken);
                    return CommandExecutionResult.Success($"Tag '{tagName}' added to session '{sessionId}'.");
                }
            }
            catch (ConversationStoreException ex)
            {
                return CommandExecutionResult.Failure(ex.Message, 3);
            }
        }

        if (string.Equals(action, "fork", StringComparison.OrdinalIgnoreCase))
        {
            if (request.Arguments.Count < 3)
            {
                return CommandExecutionResult.Failure("Session ID is required. Usage: session fork <id> [new-title].");
            }

            var sessionId = request.Arguments[2];
            var newTitle = request.Arguments.Count > 3
                ? string.Join(' ', request.Arguments.Skip(3))
                : null;

            try
            {
                var forked = await context.ConversationStore.ForkAsync(sessionId, newTitle, cancellationToken);
                var transcript = context.TranscriptRenderer.Render(forked.Messages);
                var output = $"Forked session {sessionId} -> {forked.Id}: {forked.Title}{Environment.NewLine}{transcript}";
                return CommandExecutionResult.Success(output.TrimEnd());
            }
            catch (ConversationStoreException ex)
            {
                return CommandExecutionResult.Failure(ex.Message, 3);
            }
        }

        return CommandExecutionResult.Failure("Supported session commands: session new [title], session list, session show <id>, session rename <id> <name>, session tag <id> <tag>, session fork <id> [title].");
    }
}
