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

Subcommands:
  status    Show authentication status for configured providers

Examples:
  clawdnet auth status
  clawdnet auth status --provider anthropic

Note:
  ClawdNet uses environment variables for authentication.
  Set ANTHROPIC_API_KEY, OPENAI_API_KEY, etc. before running queries.
  OAuth/keychain auth from the legacy CLI is not currently supported.
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
            "login" => CommandExecutionResult.Failure(
                "Interactive OAuth login is not currently supported. " +
                "Set provider API keys via environment variables (e.g., ANTHROPIC_API_KEY, OPENAI_API_KEY).", 1),
            "logout" => CommandExecutionResult.Failure(
                "Logout is not supported in env-var-based auth. " +
                "Unset the relevant environment variables to disable auth.", 1),
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
