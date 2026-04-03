using System.Text.Json;
using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Exceptions;
using ClawdNet.Core.Models;
using ClawdNet.Core.Serialization;

namespace ClawdNet.Core.Commands;

public sealed class AskCommandHandler : ICommandHandler
{
    public string Name => "ask";

    public string HelpSummary => "Send a prompt to the model in headless mode";

    public string HelpText => """
Usage: clawdnet ask [options] <prompt>

Sends a prompt to the model and returns the response in headless mode.

Options:
  --session <id>            Continue an existing session
  --provider <name>         Override the provider for this query
  --model <name>            Override the model for this query
  --permission-mode <mode>  Permission mode (default, accept-edits, bypass-permissions)
  --json                    Output as JSON (single object at end)
  --output-format <format>  Output format: text (default), json, stream-json
  --input-format <format>   Input format: text (default), stream-json

Examples:
  clawdnet ask "What is 2+2?"
  clawdnet ask --session abc123 "Continue"
  clawdnet ask --provider openai --model gpt-4o "Explain this code"
  clawdnet ask --json "Summarize this project"
  clawdnet ask --output-format stream-json "hello"
  echo '{"type":"user","message":{"role":"user","content":"hello"}}' | clawdnet ask --input-format stream-json --output-format stream-json
""";

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

            // Cross-flag validation
            if (options.InputFormat == "stream-json" && options.OutputFormat != "stream-json")
            {
                return CommandExecutionResult.Failure("--input-format=stream-json requires --output-format=stream-json.", 1);
            }

            // Handle structured stdin for stream-json mode
            var prompt = options.Prompt;
            if (options.InputFormat == "stream-json" && string.IsNullOrWhiteSpace(prompt))
            {
                prompt = await ReadPromptFromStructuredStdinAsync(cancellationToken);
                if (string.IsNullOrWhiteSpace(prompt))
                {
                    return CommandExecutionResult.Failure("--input-format=stream-json requires a user message on stdin.", 1);
                }
            }

            // Validate prompt is present
            if (string.IsNullOrWhiteSpace(prompt))
            {
                return CommandExecutionResult.Failure("ask requires a prompt.", 1);
            }

            if (options.OutputFormat == "stream-json")
            {
                // Install stdout guard for stream-json mode
                InstallStreamJsonStdoutGuard();

                await foreach (var streamEvent in context.QueryEngine.StreamAskAsync(
                    new QueryRequest(prompt, options.SessionId, options.Model, 8, options.PermissionMode, null, true, options.Provider),
                    cancellationToken))
                {
                    var ndjsonLine = NdjsonSerializer.Serialize(streamEvent);
                    if (ndjsonLine != null)
                    {
                        Console.WriteLine(ndjsonLine);
                    }
                }

                // stream-json writes directly to stdout, return empty success
                return CommandExecutionResult.Success(string.Empty);
            }

            var result = await context.QueryEngine.AskAsync(
                new QueryRequest(prompt, options.SessionId, options.Model, 8, options.PermissionMode, null, true, options.Provider),
                cancellationToken);

            if (options.OutputFormat == "json" || options.Json)
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
        string? outputFormat = null;
        string? inputFormat = null;
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
                case "--output-format" when index + 1 < args.Length:
                    outputFormat = ParseOutputFormat(args[++index]);
                    break;
                case "--input-format" when index + 1 < args.Length:
                    inputFormat = ParseInputFormat(args[++index]);
                    break;
                default:
                    promptParts.Add(args[index]);
                    break;
            }
        }

        var prompt = string.Join(' ', promptParts).Trim();
        if (string.IsNullOrWhiteSpace(prompt) && inputFormat != "stream-json")
        {
            throw new ConversationStoreException("ask requires a prompt.");
        }

        return new AskOptions(
            prompt ?? string.Empty,
            sessionId,
            model,
            permissionMode,
            json,
            provider,
            outputFormat ?? "text",
            inputFormat ?? "text");
    }

    private static string ParseOutputFormat(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "text" => "text",
            "json" => "json",
            "stream-json" => "stream-json",
            _ => throw new ConversationStoreException($"Unknown output format '{value}'. Use text, json, or stream-json.")
        };
    }

    private static string ParseInputFormat(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "text" => "text",
            "stream-json" => "stream-json",
            _ => throw new ConversationStoreException($"Unknown input format '{value}'. Use text or stream-json.")
        };
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

    private static async Task<string?> ReadPromptFromStructuredStdinAsync(CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(Console.OpenStandardInput());
        var line = await reader.ReadLineAsync().WaitAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        try
        {
            var element = JsonSerializer.Deserialize<JsonElement>(line);
            var type = element.GetProperty("type").GetString();
            if (type == "user" && element.TryGetProperty("message", out var message))
            {
                // Support both { role: "user", content: "..." } and direct content
                if (message.TryGetProperty("content", out var content))
                {
                    return content.GetString();
                }
            }
        }
        catch
        {
            // If parsing fails, treat the line as raw prompt text
            return line.Trim();
        }

        return null;
    }

    private static void InstallStreamJsonStdoutGuard()
    {
        // In stream-json mode, redirect Console.Error to ensure diagnostic output
        // does not corrupt the NDJSON stream on stdout.
        // This is a lightweight guard - Console.WriteLine/WriteLine still go to stdout,
        // but any Console.Error writes go to stderr as expected.
        // The main risk is accidental Console.WriteLine calls in non-NDJSON code paths.
        // We mitigate this by:
        // 1. Only installing in stream-json mode
        // 2. Using Console.Error explicitly for any diagnostic output
        // No actual redirection needed since Console.Error already goes to stderr.
        // This method exists as a placeholder for future guard enhancements.
    }

    private sealed record AskOptions(
        string Prompt,
        string? SessionId,
        string? Model,
        PermissionMode PermissionMode,
        bool Json,
        string? Provider,
        string OutputFormat,
        string InputFormat);
}
