using System.Text.Json;
using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Exceptions;
using ClawdNet.Core.Models;
using ClawdNet.Core.Serialization;
using ClawdNet.Core.Services;

namespace ClawdNet.Core.Commands;

public sealed class AskCommandHandler : ICommandHandler
{
    public string Name => "ask";

    public string HelpSummary => "Send a prompt to the model in headless mode";

    public string HelpText => """
Usage: clawdnet ask [options] <prompt>

Sends a prompt to the model and returns the response in headless mode.

Options:
  --session <id>              Continue an existing session
  --provider <name>           Override the provider for this query
  --model <name>              Override the model for this query
  --permission-mode <mode>    Permission mode (default, accept-edits, bypass-permissions)
  --json                      Output as JSON (single object at end)
  --output-format <format>    Output format: text (default), json, stream-json
  --input-format <format>     Input format: text (default), stream-json
  --allowed-tools <tools...>  Comma or space-separated list of tools to allow
  --disallowed-tools <tools...> Comma or space-separated list of tools to deny
  --system-prompt <text>      Override the system prompt for this query
  --system-prompt-file <path> Load system prompt from a file
  --settings <file-or-json>   Load settings from a file or inline JSON
  --effort <level>            Effort level: low, medium (default), high
  --thinking <mode>           Thinking mode: adaptive (default), enabled, disabled
  --max-turns <N>             Maximum number of model turns (default: 8)
  --max-budget-usd <N>        Maximum cost budget in USD

Examples:
  clawdnet ask "What is 2+2?"
  clawdnet ask --session abc123 "Continue"
  clawdnet ask --provider openai --model gpt-4o "Explain this code"
  clawdnet ask --json "Summarize this project"
  clawdnet ask --output-format stream-json "hello"
  echo '{"type":"user","message":{"role":"user","content":"hello"}}' | clawdnet ask --input-format stream-json --output-format stream-json
  clawdnet ask --allowed-tools "echo,grep" "hello"
  clawdnet ask --disallowed-tools "shell" "inspect this"
  clawdnet ask --system-prompt "You are a helpful coding assistant" "explain this"
  clawdnet ask --system-prompt-file /path/to/prompt.txt "explain this"
  clawdnet ask --effort high "Explain quantum computing in detail"
  clawdnet ask --thinking enabled "Solve this complex math problem"
  clawdnet ask --max-turns 3 "Quick question"
  clawdnet ask --max-budget-usd 0.01 "Write a summary"
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

            // Load and merge settings from --settings (app-native settings only)
            var (mergedAllowedTools, mergedDisallowedTools, systemPrompt) = await LoadSettingsAndMemoryAsync(
                context, options, cancellationToken);

            // Merge tool lists: explicit flags win over settings
            var finalAllowedTools = MergeToolLists(options.AllowedTools, mergedAllowedTools);
            var finalDisallowedTools = MergeToolLists(options.DisallowedTools, mergedDisallowedTools);

            if (options.OutputFormat == "stream-json")
            {
                // Install stdout guard for stream-json mode
                InstallStreamJsonStdoutGuard();

                await foreach (var streamEvent in context.QueryEngine.StreamAskAsync(
                    new QueryRequest(
                        prompt,
                        options.SessionId,
                        options.Model,
                        options.MaxTurns,
                        options.PermissionMode,
                        null,
                        true,
                        options.Provider,
                        finalAllowedTools,
                        finalDisallowedTools,
                        systemPrompt,
                        options.SettingsFile,
                        options.Effort,
                        options.Thinking,
                        options.MaxBudgetUsd),
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
                new QueryRequest(
                    prompt,
                    options.SessionId,
                    options.Model,
                    options.MaxTurns,
                    options.PermissionMode,
                    null,
                    true,
                    options.Provider,
                    finalAllowedTools,
                    finalDisallowedTools,
                    systemPrompt,
                    options.SettingsFile,
                    options.Effort,
                    options.Thinking,
                    options.MaxBudgetUsd),
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

    private async Task<(IReadOnlyCollection<string>? AllowedTools, IReadOnlyCollection<string>? DisallowedTools, string? SystemPrompt)> LoadSettingsAndMemoryAsync(
        CommandContext context,
        AskOptions options,
        CancellationToken cancellationToken)
    {
        var allowedTools = new List<string>();
        var disallowedTools = new List<string>();

        _ = cancellationToken;

        // Load settings from --settings file/JSON only (app-native settings)
        if (!string.IsNullOrWhiteSpace(options.SettingsFile))
        {
            var settings = LoadSettingsFromString(options.SettingsFile);
            if (settings is not null)
            {
                ExtractToolSettings(settings, allowedTools, disallowedTools);
            }
        }

        return (
            allowedTools.Count > 0 ? allowedTools : null,
            disallowedTools.Count > 0 ? disallowedTools : null,
            options.SystemPrompt);
    }

    private static Dictionary<string, object?>? LoadSettingsFromString(string value)
    {
        // If it looks like JSON, parse it directly
        if (value.TrimStart().StartsWith("{", StringComparison.Ordinal))
        {
            try
            {
                using var doc = JsonDocument.Parse(value);
                var result = new Dictionary<string, object?>();
                foreach (var property in doc.RootElement.EnumerateObject())
                {
                    result[property.Name] = ConvertJsonValue(property.Value);
                }
                return result;
            }
            catch (JsonException)
            {
                // If inline JSON fails, treat it as a file path
            }
        }

        // Treat as file path
        try
        {
            if (!File.Exists(value))
            {
                return null;
            }

            var content = File.ReadAllText(value);
            using var doc2 = JsonDocument.Parse(content);
            var result2 = new Dictionary<string, object?>();
            foreach (var property in doc2.RootElement.EnumerateObject())
            {
                result2[property.Name] = ConvertJsonValue(property.Value);
            }
            return result2;
        }
        catch
        {
            return null;
        }
    }

    private static object? ConvertJsonValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Array => element.EnumerateArray().Select(e => ConvertJsonValue(e)).ToList(),
            JsonValueKind.Object => element.ToString(),
            _ => null
        };
    }

    private static void ExtractToolSettings(
        Dictionary<string, object?> settings,
        List<string> allowedTools,
        List<string> disallowedTools)
    {
        // Extract allowedTools/allowed-tools
        if (settings.TryGetValue("allowedTools", out var allowed) && allowed is List<object?> allowedList)
        {
            allowedTools.AddRange(allowedList.OfType<string>());
        }
        else if (settings.TryGetValue("allowed-tools", out var allowed2) && allowed2 is List<object?> allowedList2)
        {
            allowedTools.AddRange(allowedList2.OfType<string>());
        }

        // Extract disallowedTools/disallowed-tools
        if (settings.TryGetValue("disallowedTools", out var disallowed) && disallowed is List<object?> disallowedList)
        {
            disallowedTools.AddRange(disallowedList.OfType<string>());
        }
        else if (settings.TryGetValue("disallowed-tools", out var disallowed2) && disallowed2 is List<object?> disallowedList2)
        {
            disallowedTools.AddRange(disallowedList2.OfType<string>());
        }

        // Extract base tools allowlist
        if (settings.TryGetValue("tools", out var tools) && tools is List<object?> toolsList)
        {
            allowedTools.AddRange(toolsList.OfType<string>());
        }
    }

    private static IReadOnlyCollection<string>? MergeToolLists(
        IReadOnlyCollection<string>? explicitList,
        IReadOnlyCollection<string>? settingsList)
    {
        if (explicitList is not null && explicitList.Count > 0)
        {
            // Explicit flags win over settings
            return explicitList;
        }

        if (settingsList is not null && settingsList.Count > 0)
        {
            return settingsList;
        }

        return null;
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
        var allowedTools = new List<string>();
        var disallowedTools = new List<string>();
        string? systemPrompt = null;
        string? systemPromptFile = null;
        string? settingsFile = null;
        var promptParts = new List<string>();
        EffortLevel? effort = null;
        ThinkingMode? thinking = null;
        int maxTurns = 8;
        decimal? maxBudgetUsd = null;

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
                case "--allowed-tools" when index + 1 < args.Length:
                    allowedTools.AddRange(ParseToolList(args[++index]));
                    break;
                case "--disallowed-tools" when index + 1 < args.Length:
                    disallowedTools.AddRange(ParseToolList(args[++index]));
                    break;
                case "--system-prompt" when index + 1 < args.Length:
                    systemPrompt = args[++index];
                    break;
                case "--system-prompt-file" when index + 1 < args.Length:
                    systemPromptFile = args[++index];
                    break;
                case "--settings" when index + 1 < args.Length:
                    settingsFile = args[++index];
                    break;
                case "--effort" when index + 1 < args.Length:
                    effort = ParseEffortLevel(args[++index]);
                    break;
                case "--thinking" when index + 1 < args.Length:
                    thinking = ParseThinkingMode(args[++index]);
                    break;
                case "--max-turns" when index + 1 < args.Length:
                    maxTurns = int.Parse(args[++index]);
                    break;
                case "--max-budget-usd" when index + 1 < args.Length:
                    maxBudgetUsd = decimal.Parse(args[++index]);
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

        // Load system prompt from file if specified
        if (!string.IsNullOrWhiteSpace(systemPromptFile))
        {
            if (!File.Exists(systemPromptFile))
            {
                throw new ConversationStoreException($"System prompt file not found: {systemPromptFile}");
            }
            systemPrompt = File.ReadAllText(systemPromptFile);
        }

        return new AskOptions(
            prompt ?? string.Empty,
            sessionId,
            model,
            permissionMode,
            json,
            provider,
            outputFormat ?? "text",
            inputFormat ?? "text",
            allowedTools.Count > 0 ? allowedTools : null,
            disallowedTools.Count > 0 ? disallowedTools : null,
            systemPrompt,
            settingsFile,
            effort,
            thinking,
            maxTurns,
            maxBudgetUsd);
    }

    private static List<string> ParseToolList(string value)
    {
        // Support comma-separated and space-separated tool names
        return value
            .Split([',', ' '], StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim())
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToList();
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

    private static EffortLevel ParseEffortLevel(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "low" => EffortLevel.Low,
            "medium" => EffortLevel.Medium,
            "high" => EffortLevel.High,
            _ => throw new ConversationStoreException($"Unknown effort level '{value}'. Use low, medium, or high.")
        };
    }

    private static ThinkingMode ParseThinkingMode(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "adaptive" => ThinkingMode.Adaptive,
            "enabled" => ThinkingMode.Enabled,
            "disabled" => ThinkingMode.Disabled,
            _ => throw new ConversationStoreException($"Unknown thinking mode '{value}'. Use adaptive, enabled, or disabled.")
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
        string InputFormat,
        IReadOnlyCollection<string>? AllowedTools,
        IReadOnlyCollection<string>? DisallowedTools,
        string? SystemPrompt,
        string? SettingsFile,
        EffortLevel? Effort,
        ThinkingMode? Thinking,
        int MaxTurns,
        decimal? MaxBudgetUsd);
}
