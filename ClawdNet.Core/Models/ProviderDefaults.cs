namespace ClawdNet.Core.Models;

public static class ProviderDefaults
{
    public const string DefaultProviderName = "anthropic";
    public const string DefaultAnthropicModel = "claude-sonnet-4-5";

    public static IReadOnlyList<ProviderDefinition> GetBuiltInProviders()
    {
        return
        [
            new ProviderDefinition(DefaultProviderName, ProviderKind.Anthropic, true, "ANTHROPIC_API_KEY", DefaultModel: DefaultAnthropicModel),
            new ProviderDefinition("openai", ProviderKind.OpenAI, true, "OPENAI_API_KEY"),
            new ProviderDefinition("bedrock", ProviderKind.Bedrock, true, "AWS_ACCESS_KEY_ID", DefaultModel: "anthropic.claude-sonnet-4-5-20250514-v1:0"),
            new ProviderDefinition("vertex", ProviderKind.VertexAI, true, "GOOGLE_APPLICATION_CREDENTIALS", DefaultModel: "claude-sonnet-4-5@20250929")
        ];
    }
}
