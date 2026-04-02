namespace ClawdNet.Core.Models;

public sealed record ProviderDefinition(
    string Name,
    ProviderKind Kind,
    bool Enabled,
    string ApiKeyEnvironmentVariable,
    string? BaseUrl = null,
    string? DefaultModel = null);
