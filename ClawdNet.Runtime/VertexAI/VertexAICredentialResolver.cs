using System.Text.Json;

namespace ClawdNet.Runtime.VertexAI;

/// <summary>
/// Resolves GCP credentials and region configuration for Vertex AI API access.
/// Supports Application Default Credentials via service account key file, project ID configuration, and skip-auth mode.
/// </summary>
public sealed class VertexAICredentialResolver
{
    private readonly string? _serviceAccountKeyPath;
    private readonly bool _skipAuth;

    public string ProjectId { get; }
    public string Region { get; }

    public VertexAICredentialResolver()
        : this(
            serviceAccountKeyPath: Environment.GetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS"),
            projectId: Environment.GetEnvironmentVariable("ANTHROPIC_VERTEX_PROJECT_ID")
                     ?? Environment.GetEnvironmentVariable("GOOGLE_CLOUD_PROJECT")
                     ?? Environment.GetEnvironmentVariable("GCLOUD_PROJECT"),
            skipAuth: Environment.GetEnvironmentVariable("CLAUDE_CODE_SKIP_VERTEX_AUTH") is "1" or "true",
            region: Environment.GetEnvironmentVariable("CLOUD_ML_REGION"))
    {
    }

    public VertexAICredentialResolver(
        string? serviceAccountKeyPath,
        string? projectId,
        bool skipAuth,
        string? region)
    {
        _serviceAccountKeyPath = serviceAccountKeyPath;
        _skipAuth = skipAuth;
        ProjectId = string.IsNullOrWhiteSpace(projectId) ? string.Empty : projectId.Trim();
        Region = string.IsNullOrWhiteSpace(region) ? "us-east5" : region.Trim();
    }

    /// <summary>
    /// Returns the region for a specific model, respecting per-model override env vars.
    /// </summary>
    public string GetRegionForModel(string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            return Region;
        }

        var modelLower = model.ToLowerInvariant();
        var overrides = new (string Prefix, string EnvVar)[]
        {
            ("claude-haiku-4-5", "VERTEX_REGION_CLAUDE_HAIKU_4_5"),
            ("claude-3-5-haiku", "VERTEX_REGION_CLAUDE_3_5_HAIKU"),
            ("claude-3-5-sonnet", "VERTEX_REGION_CLAUDE_3_5_SONNET"),
            ("claude-3-7-sonnet", "VERTEX_REGION_CLAUDE_3_7_SONNET"),
            ("claude-opus-4-1", "VERTEX_REGION_CLAUDE_4_1_OPUS"),
            ("claude-opus-4", "VERTEX_REGION_CLAUDE_4_0_OPUS"),
            ("claude-sonnet-4-6", "VERTEX_REGION_CLAUDE_4_6_SONNET"),
            ("claude-sonnet-4-5", "VERTEX_REGION_CLAUDE_4_5_SONNET"),
            ("claude-sonnet-4", "VERTEX_REGION_CLAUDE_4_0_SONNET"),
        };

        foreach (var (prefix, envVar) in overrides)
        {
            if (modelLower.StartsWith(prefix, StringComparison.Ordinal))
            {
                var envRegion = Environment.GetEnvironmentVariable(envVar);
                if (!string.IsNullOrWhiteSpace(envRegion))
                {
                    return envRegion.Trim();
                }
                break;
            }
        }

        return Region;
    }

    public bool HasCredentials => _skipAuth ||
        (!string.IsNullOrWhiteSpace(_serviceAccountKeyPath) && File.Exists(_serviceAccountKeyPath));

    public bool UseSkipAuth => _skipAuth;

    /// <summary>
    /// Loads the service account key JSON and returns the client_email and private_key.
    /// </summary>
    public (string ClientEmail, string PrivateKey)? LoadServiceAccountKey()
    {
        if (string.IsNullOrWhiteSpace(_serviceAccountKeyPath) || !File.Exists(_serviceAccountKeyPath))
        {
            return null;
        }

        using var stream = File.OpenRead(_serviceAccountKeyPath!);
        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;

        var clientEmail = root.TryGetProperty("client_email", out var ce) ? ce.GetString() : null;
        var privateKey = root.TryGetProperty("private_key", out var pk) ? pk.GetString() : null;

        if (string.IsNullOrWhiteSpace(clientEmail) || string.IsNullOrWhiteSpace(privateKey))
        {
            return null;
        }

        return (clientEmail!, privateKey!);
    }
}
