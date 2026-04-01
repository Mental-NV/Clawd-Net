using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Models;

namespace ClawdNet.Core.Commands;

public sealed class SessionCommandHandler : ICommandHandler
{
    public string Name => "session";

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
            var title = request.Arguments.Count > 2
                ? string.Join(' ', request.Arguments.Skip(2))
                : null;
            var session = await context.ConversationStore.CreateAsync(title, "claude-sonnet-4-5", cancellationToken);
            var transcript = context.TranscriptRenderer.Render(session.Messages);
            var output = $"Created session {session.Id}: {session.Title}{Environment.NewLine}{transcript}";
            return CommandExecutionResult.Success(output.TrimEnd());
        }

        if (string.Equals(action, "list", StringComparison.OrdinalIgnoreCase))
        {
            var sessions = await context.ConversationStore.ListAsync(cancellationToken);
            if (sessions.Count == 0)
            {
                return CommandExecutionResult.Success("No sessions found.");
            }

            var lines = sessions.Select(session => $"{session.Id} | {session.Title} | {session.UpdatedAtUtc:O} | {session.Model}");
            return CommandExecutionResult.Success(string.Join(Environment.NewLine, lines));
        }

        return CommandExecutionResult.Failure("Supported session commands: session new [title], session list.");
    }
}
