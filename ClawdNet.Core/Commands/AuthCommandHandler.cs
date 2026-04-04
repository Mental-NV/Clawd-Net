using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Models;

namespace ClawdNet.Core.Commands;

public sealed class AuthCommandHandler : ICommandHandler
{
    private readonly IProviderCatalog _providerCatalog;
    private readonly IOAuthService? _oauthService;

    public AuthCommandHandler(IProviderCatalog providerCatalog, IOAuthService? oauthService = null)
    {
        _providerCatalog = providerCatalog;
        _oauthService = oauthService;
    }

    public string Name => "auth";

    public string HelpSummary => "Manage authentication for providers";

    public string HelpText => """
Usage: clawdnet auth <subcommand> [options]

Manage authentication credentials for model providers.

ClawdNet supports both environment variable authentication and interactive
OAuth login for the Anthropic provider.

Subcommands:
  status    Show authentication status for configured providers
  login     Authenticate with a provider (OAuth or env-var guidance)
  logout    Clear stored OAuth tokens and guidance for env vars

Options (login):
  --browser           Launch browser for OAuth login
  --provider <name>   Provider to authenticate (default: anthropic)
  --port <number>     Callback server port (default: 9876)

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
  clawdnet auth login --browser
  clawdnet auth login --provider anthropic --browser
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
            "login" => await ExecuteLoginAsync(context, request, cancellationToken),
            "logout" => await ExecuteLogoutAsync(context, request, cancellationToken),
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

            // Check OAuth token status for Anthropic provider
            string? oauthInfo = null;
            if (_oauthService != null && (string.IsNullOrWhiteSpace(filterProvider) ||
                filterProvider.Equals("anthropic", StringComparison.OrdinalIgnoreCase)))
            {
                var accountInfo = await _oauthService.GetAccountInfoAsync(cancellationToken);
                if (accountInfo != null)
                {
                    var parts = new List<string> { "OAuth token: active" };
                    if (!string.IsNullOrEmpty(accountInfo.Email))
                        parts.Add($"email: {accountInfo.Email}");
                    if (!string.IsNullOrEmpty(accountInfo.SubscriptionType))
                        parts.Add($"subscription: {accountInfo.SubscriptionType}");
                    oauthInfo = string.Join(", ", parts);
                }
                else
                {
                    oauthInfo = "OAuth token: not present";
                }
            }

            if (results.Count == 0 && !string.IsNullOrWhiteSpace(filterProvider))
            {
                return CommandExecutionResult.Failure($"Provider '{filterProvider}' not found.", 3);
            }

            var output = FormatAuthStatus(results, oauthInfo);
            return CommandExecutionResult.Success(output);
        }
        catch (Exception ex)
        {
            return CommandExecutionResult.Failure($"Failed to check auth status: {ex.Message}", 1);
        }
    }

    private async Task<CommandExecutionResult> ExecuteLoginAsync(
        CommandContext context,
        CommandRequest request,
        CancellationToken cancellationToken)
    {
        var useBrowser = request.Arguments.Contains("--browser", StringComparer.OrdinalIgnoreCase);
        var providerName = request.Arguments
            .Skip(2)
            .Where((arg, i) => i == 0 || request.Arguments.ElementAtOrDefault(i - 1) == "--provider")
            .FirstOrDefault(arg => arg != "--provider")
            ?? "anthropic";
        var callbackPort = request.Arguments
            .Skip(2)
            .Where((arg, i) => i == 0 || request.Arguments.ElementAtOrDefault(i - 1) == "--port")
            .FirstOrDefault(arg => arg != "--port");

        // If --browser flag is present, attempt OAuth login
        if (useBrowser)
        {
            if (_oauthService == null || !_oauthService.IsSupported)
            {
                return CommandExecutionResult.Failure(
                    "OAuth login is not available. Please set provider API keys via environment variables.", 1);
            }

            try
            {
                var options = new OAuthLoginOptions();
                OAuthAccountInfo accountInfo;
                if (!string.IsNullOrEmpty(callbackPort) && int.TryParse(callbackPort, out var port))
                {
                    accountInfo = await _oauthService.LoginAsync(options with { CallbackPort = port }, cancellationToken);
                }
                else
                {
                    accountInfo = await _oauthService.LoginAsync(options, cancellationToken);
                }

                var lines = new List<string>
                {
                    "Authentication successful!",
                    string.Empty,
                    $"  Email: {accountInfo.Email}",
                    $"  Organization: {(string.IsNullOrEmpty(accountInfo.Organization) ? "—" : accountInfo.Organization)}",
                    $"  Subscription: {(string.IsNullOrEmpty(accountInfo.SubscriptionType) ? "—" : accountInfo.SubscriptionType)}",
                    string.Empty,
                    "Your OAuth tokens have been saved securely."
                };
                return CommandExecutionResult.Success(string.Join(Environment.NewLine, lines));
            }
            catch (OperationCanceledException)
            {
                return CommandExecutionResult.Failure("OAuth login was cancelled.", 1);
            }
            catch (TimeoutException ex)
            {
                return CommandExecutionResult.Failure(ex.Message, 1);
            }
            catch (Exception ex)
            {
                return CommandExecutionResult.Failure($"OAuth login failed: {ex.Message}", 1);
            }
        }

        // Without --browser, show env-var guidance
        var provider = (await _providerCatalog.ListAsync(cancellationToken))
            .FirstOrDefault(p => p.Name.Equals(providerName, StringComparison.OrdinalIgnoreCase));

        if (provider == null)
        {
            return CommandExecutionResult.Failure($"Provider '{providerName}' not found.", 3);
        }

        var envVar = provider.ApiKeyEnvironmentVariable ?? "not configured";
        return CommandExecutionResult.Success(
            $"To authenticate with {provider.Name}, set the following environment variable:\n\n" +
            $"  export {envVar}=your-api-key\n\n" +
            $"Run 'clawdnet auth status' to verify your configuration.\n\n" +
            $"For interactive browser-based login, use:\n" +
            $"  clawdnet auth login --browser");
    }

    private async Task<CommandExecutionResult> ExecuteLogoutAsync(
        CommandContext context,
        CommandRequest request,
        CancellationToken cancellationToken)
    {
        // Clear OAuth tokens if available
        if (_oauthService != null)
        {
            await _oauthService.LogoutAsync(cancellationToken);
        }

        return CommandExecutionResult.Success(
            "OAuth tokens have been cleared.\n\n" +
            "If you are using environment variable authentication, unset the relevant variables:\n" +
            "  unset ANTHROPIC_API_KEY OPENAI_API_KEY\n\n" +
            "Run 'clawdnet auth status' to verify you are logged out.");
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

    private static string FormatAuthStatus(IReadOnlyList<ProviderAuthStatus> results, string? oauthInfo)
    {
        if (results.Count == 0 && oauthInfo == null)
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

        if (!string.IsNullOrEmpty(oauthInfo))
        {
            lines.Add($"  {oauthInfo}");
            lines.Add(string.Empty);
        }

        lines.Add("Authentication methods:");
        lines.Add("  - Environment variables (all providers)");
        lines.Add("  - OAuth browser login (Anthropic: 'clawdnet auth login --browser')");

        return string.Join(Environment.NewLine, lines).TrimEnd();
    }

    private sealed record ProviderAuthStatus(
        string Name,
        string Kind,
        bool IsConfigured,
        string ConfigStatus,
        string? ApiKeyEnvVar);
}
