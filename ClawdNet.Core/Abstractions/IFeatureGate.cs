namespace ClawdNet.Core.Abstractions;

public interface IFeatureGate
{
    bool IsEnabled(string featureName);
}
