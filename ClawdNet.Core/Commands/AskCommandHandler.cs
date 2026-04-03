using System.Text.Json;
using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Exceptions;
using ClawdNet.Core.Models;

namespace ClawdNet.Core.Commands;

public sealed class AskCommandHandler : ICommandHandler
{
    public string Name => "ask";

    public bool CanHandle(CommandRequest request)
    {
        return request.Arguments.Count > 0
            && string.Equals(request.Arguments[0], "ask", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<CommandExecutionResult> ExecuteAsync(
        CommandContext context,
        CommandRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var options = Parse(request.Arguments.Skip(1).ToArray());
            var result = await context.QueryEngine.AskAsync(
                new QueryRequest(options.Prompt, options.SessionId, options.Model, 8, options.PermissionMode, null, true, options.Provider),
                cancellationToken);

            if (options.Json)
            {
                var payload = JsonSerializer.Serialize(new
                {
                    sessionId = result.Session.Id,
                    provider = result.Session.Provider,
                    model = result.Session.Model,
                    turnsExecuted = result.TurnsExecuted,
                    assistantText = result.AssistantText,
                    updatedAtUtc = result.Session.UpdatedAtUtc
                });
                return CommandExecutionResult.Success(payload);
            }

            var output = string.Join(
                Environment.NewLine,
                [
                    $"Session: {result.Session.Id}",
                    $"Model: {result.Session.Model}",
                    string.Empty,
                    result.AssistantText
                ]);
            return CommandExecutionResult.Success(output.TrimEnd());
        }
        catch (ModelProviderConfigurationException ex)
        {
            return CommandExecutionResult.Failure(ex.Message, 2);
        }
        catch (ConversationStoreException ex)
        {
            return CommandExecutionResult.Failure(ex.Message, 3);
        }
    }

    private static AskOptions Parse(string[] args)
    {
        string? sessionId = null;
        string? provider = null;
        string? model = null;
        var permissionMode = PermissionMode.Default;
        var json = false;
        var promptParts = new List<string>();

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--session" when index + 1 < args.Length:
                    sessionId = args[++index];
                    break;
                case "--provider" when index + 1 < args.Length:
                    provider = args[++index];
                    break;
                case "--model" when index + 1 < args.Length:
                    model = args[++index];
                    break;
                case "--permission-mode" when index + 1 < args.Length:
                    permissionMode = ParsePermissionMode(args[++index]);
                    break;
                case "--json":
                    json = true;
                    break;
                default:
                    promptParts.Add(args[index]);
                    break;
            }
        }

        var prompt = string.Join(' ', promptParts).Trim();
        if (string.IsNullOrWhiteSpace(prompt))
        {
            throw new ConversationStoreException("ask requires a prompt.");
        }

        return new AskOptions(prompt, sessionId, model, permissionMode, json, provider);
    }

    private static PermissionMode ParsePermissionMode(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "default" => PermissionMode.Default,
            "acceptedits" => PermissionMode.AcceptEdits,
            "accept-edits" => PermissionMode.AcceptEdits,
            "bypasspermissions" => PermissionMode.BypassPermissions,
            "bypass-permissions" => PermissionMode.BypassPermissions,
            "bypass" => PermissionMode.BypassPermissions,
            _ => throw new ConversationStoreException($"Unknown permission mode '{value}'.")
        };
    }

    private sealed record AskOptions(string Prompt, string? SessionId, string? Model, PermissionMode PermissionMode, bool Json, string? Provider);
}
