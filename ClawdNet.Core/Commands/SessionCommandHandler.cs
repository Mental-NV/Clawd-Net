using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Exceptions;
using ClawdNet.Core.Models;

namespace ClawdNet.Core.Commands;

public sealed class SessionCommandHandler : ICommandHandler
{
    public string Name => "session";

    public string HelpSummary => "Create, list, and inspect conversation sessions";

    public string HelpText => """
Usage: clawdnet session new [title]
       clawdnet session list
       clawdnet session show <id>

Manage conversation sessions.

Commands:
  new [title]    Create a new session with an optional title
  list           List all sessions
  show <id>      Show session details including recent messages

Examples:
  clawdnet session new "Debug Session"
  clawdnet session list
  clawdnet session show abc123def
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
                string.Empty,
                "Recent messages:"
            };

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

        return CommandExecutionResult.Failure("Supported session commands: session new [title], session list, session show <id>.");
    }
}
