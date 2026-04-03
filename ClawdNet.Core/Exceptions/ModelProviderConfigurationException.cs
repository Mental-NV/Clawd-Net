namespace ClawdNet.Core.Exceptions;

public sealed class ModelProviderConfigurationException : Exception
{
    public ModelProviderConfigurationException(string providerName, string message)
        : base($"{providerName}: {message}")
    {
        ProviderName = providerName;
    }

    public string ProviderName { get; }
}
