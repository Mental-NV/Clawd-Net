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
            new ProviderDefinition("openai", ProviderKind.OpenAI, true, "OPENAI_API_KEY")
        ];
    }
}
