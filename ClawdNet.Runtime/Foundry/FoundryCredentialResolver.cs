namespace ClawdNet.Runtime.Foundry;

/// <summary>
/// Resolves Azure Foundry credentials and endpoint configuration.
/// Supports API key auth and skip-auth mode.
/// </summary>
public sealed class FoundryCredentialResolver
{
    private readonly string? _apiKey;
    private readonly bool _skipAuth;

    public string? ResourceName { get; }
    public string? CustomBaseUrl { get; }

    public FoundryCredentialResolver()
        : this(
            apiKey: Environment.GetEnvironmentVariable("ANTHROPIC_FOUNDRY_API_KEY"),
            skipAuth: Environment.GetEnvironmentVariable("CLAUDE_CODE_SKIP_FOUNDRY_AUTH") is "1" or "true",
            resourceName: Environment.GetEnvironmentVariable("ANTHROPIC_FOUNDRY_RESOURCE"),
            customBaseUrl: Environment.GetEnvironmentVariable("ANTHROPIC_FOUNDRY_BASE_URL"))
    {
    }

    public FoundryCredentialResolver(
        string? apiKey,
        bool skipAuth,
        string? resourceName,
        string? customBaseUrl)
    {
        _apiKey = apiKey;
        _skipAuth = skipAuth;
        ResourceName = string.IsNullOrWhiteSpace(resourceName) ? null : resourceName.Trim();
        CustomBaseUrl = string.IsNullOrWhiteSpace(customBaseUrl) ? null : customBaseUrl.Trim();
    }

    public bool HasCredentials => _skipAuth || !string.IsNullOrWhiteSpace(_apiKey);

    public bool UseSkipAuth => _skipAuth;

    public string? GetApiKey() => _apiKey;

    /// <summary>
    /// Builds the Foundry API base URL.
    /// Uses custom base URL if set, otherwise constructs from resource name.
    /// Format: https://{resource}.services.ai.azure.com/anthropic
    /// </summary>
    public string BuildBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(CustomBaseUrl))
        {
            return CustomBaseUrl.TrimEnd('/');
        }

        if (!string.IsNullOrWhiteSpace(ResourceName))
        {
            return $"https://{ResourceName}.services.ai.azure.com/anthropic";
        }

        // Default — will fail clearly if neither resource nor custom URL is set
        return string.Empty;
    }
}
