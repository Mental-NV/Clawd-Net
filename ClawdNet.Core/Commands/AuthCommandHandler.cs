using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Models;

namespace ClawdNet.Core.Commands;

public sealed class AuthCommandHandler : ICommandHandler
{
    private readonly IProviderCatalog _providerCatalog;

    public AuthCommandHandler(IProviderCatalog providerCatalog)
    {
        _providerCatalog = providerCatalog;
    }

    public string Name => "auth";

    public string HelpSummary => "Manage authentication for providers";

    public string HelpText => """
Usage: clawdnet auth <subcommand> [options]

Manage authentication credentials for model providers.

ClawdNet uses environment variables for provider authentication.
OAuth/keychain auth from the legacy CLI is intentionally not supported.

Subcommands:
  status    Show authentication status for configured providers
  login     Guidance on setting provider API keys
  logout    Guidance on unsetting provider API keys

Environment Variables:
  Anthropic:    ANTHROPIC_API_KEY, ANTHROPIC_BASE_URL
  OpenAI:       OPENAI_API_KEY, OPENAI_BASE_URL
  AWS Bedrock:  AWS_ACCESS_KEY_ID, AWS_SECRET_ACCESS_KEY, AWS_REGION
                AWS_BEARER_TOKEN_BEDROCK, CLAUDE_CODE_SKIP_BEDROCK_AUTH
  Vertex AI:    GOOGLE_APPLICATION_CREDENTIALS, ANTHROPIC_VERTEX_PROJECT_ID
                CLOUD_ML_REGION, CLAUDE_CODE_SKIP_VERTEX_AUTH
  Azure Foundry: ANTHROPIC_FOUNDRY_API_KEY, ANTHROPIC_FOUNDRY_RESOURCE
                 CLAUDE_CODE_SKIP_FOUNDRY_AUTH

Examples:
  clawdnet auth status
  clawdnet auth status --provider anthropic
  clawdnet auth login
  clawdnet auth logout
""";

    public bool CanHandle(CommandRequest request)
    {
        return request.Arguments.Count > 0
            && string.Equals(request.Arguments[0], "auth", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<CommandExecutionResult> ExecuteAsync(
        CommandContext context,
        CommandRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Arguments.Count < 2)
        {
            return CommandExecutionResult.Failure("auth requires a subcommand. Use 'clawdnet auth --help' for usage.", 1);
        }

        var subcommand = request.Arguments[1].ToLowerInvariant();

        return subcommand switch
        {
            "status" => await ExecuteStatusAsync(context, request, cancellationToken),
            "login" => CommandExecutionResult.Success(
                "ClawdNet uses environment variables for provider authentication.\n" +
                "OAuth/keychain auth from the legacy CLI is intentionally not supported.\n\n" +
                "To authenticate, set the appropriate environment variables for your provider:\n" +
                "  Anthropic:    export ANTHROPIC_API_KEY=your-key\n" +
                "  OpenAI:       export OPENAI_API_KEY=your-key\n" +
                "  AWS Bedrock:  export AWS_ACCESS_KEY_ID=... AWS_SECRET_ACCESS_KEY=... AWS_REGION=us-east-1\n" +
                "  Vertex AI:    export GOOGLE_APPLICATION_CREDENTIALS=/path/to/key.json\n" +
                "  Azure Foundry: export ANTHROPIC_FOUNDRY_API_KEY=your-key\n\n" +
                "Run 'clawdnet auth status' to verify your configuration."),
            "logout" => CommandExecutionResult.Success(
                "ClawdNet uses environment variables for provider authentication.\n" +
                "To logout, unset the relevant environment variables:\n" +
                "  unset ANTHROPIC_API_KEY OPENAI_API_KEY\n\n" +
                "Run 'clawdnet auth status' to verify you are logged out."),
            _ => CommandExecutionResult.Failure($"Unknown auth subcommand '{subcommand}'. Use status, login, or logout.", 1)
        };
    }

    private async Task<CommandExecutionResult> ExecuteStatusAsync(
        CommandContext context,
        CommandRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var providers = await _providerCatalog.ListAsync(cancellationToken);
            var filterProvider = request.Arguments
                .Skip(2)
                .Where((arg, i) => i == 0 || request.Arguments.ElementAtOrDefault(i - 1) == "--provider")
                .FirstOrDefault(arg => arg != "--provider");

            var results = new List<ProviderAuthStatus>();

            foreach (var provider in providers)
            {
                // If filtering to a specific provider, skip others
                if (!string.IsNullOrWhiteSpace(filterProvider) &&
                    !provider.Name.Equals(filterProvider, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var status = CheckProviderAuthStatus(provider);
                results.Add(status);
            }

            if (results.Count == 0 && !string.IsNullOrWhiteSpace(filterProvider))
            {
                return CommandExecutionResult.Failure($"Provider '{filterProvider}' not found.", 3);
            }

            var output = FormatAuthStatus(results);
            return CommandExecutionResult.Success(output);
        }
        catch (Exception ex)
        {
            return CommandExecutionResult.Failure($"Failed to check auth status: {ex.Message}", 1);
        }
    }

    private static ProviderAuthStatus CheckProviderAuthStatus(ProviderDefinition provider)
    {
        var apiKeyEnvVar = provider.ApiKeyEnvironmentVariable;
        var isConfigured = false;
        var configStatus = "not configured";

        if (!string.IsNullOrWhiteSpace(apiKeyEnvVar))
        {
            var apiKey = Environment.GetEnvironmentVariable(apiKeyEnvVar);
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                isConfigured = true;
                configStatus = $"configured ({apiKeyEnvVar} set)";
            }
            else
            {
                configStatus = $"missing ({apiKeyEnvVar} not set)";
            }
        }
        else
        {
            configStatus = "no env var configured";
        }

        return new ProviderAuthStatus(
            provider.Name,
            provider.Kind.ToString(),
            isConfigured,
            configStatus,
            apiKeyEnvVar);
    }

    private static string FormatAuthStatus(IReadOnlyList<ProviderAuthStatus> results)
    {
        if (results.Count == 0)
        {
            return "No providers configured.";
        }

        var lines = new List<string>
        {
            "Provider Authentication Status:",
            string.Empty
        };

        foreach (var result in results)
        {
            var statusIcon = result.IsConfigured ? "✓" : "✗";
            lines.Add($"  {statusIcon} {result.Name} ({result.Kind})");
            lines.Add($"    Status: {result.ConfigStatus}");
            if (!string.IsNullOrWhiteSpace(result.ApiKeyEnvVar))
            {
                lines.Add($"    Env Var: {result.ApiKeyEnvVar}");
            }
            lines.Add(string.Empty);
        }

        lines.Add("Note: ClawdNet uses environment variables for authentication.");
        lines.Add("OAuth/keychain auth from the legacy CLI is not currently supported.");

        return string.Join(Environment.NewLine, lines).TrimEnd();
    }

    private sealed record ProviderAuthStatus(
        string Name,
        string Kind,
        bool IsConfigured,
        string ConfigStatus,
        string? ApiKeyEnvVar);
}
