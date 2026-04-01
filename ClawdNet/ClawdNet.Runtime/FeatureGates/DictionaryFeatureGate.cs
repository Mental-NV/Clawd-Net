using ClawdNet.Core.Abstractions;

namespace ClawdNet.Runtime.FeatureGates;

public sealed class DictionaryFeatureGate : IFeatureGate
{
    private readonly HashSet<string> _enabledFeatures;

    public DictionaryFeatureGate(IEnumerable<string>? enabledFeatures = null)
    {
        _enabledFeatures = new HashSet<string>(enabledFeatures ?? [], StringComparer.OrdinalIgnoreCase);
    }

    public bool IsEnabled(string featureName) => _enabledFeatures.Contains(featureName);
}
