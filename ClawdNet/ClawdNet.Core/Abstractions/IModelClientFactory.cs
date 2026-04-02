using ClawdNet.Core.Models;

namespace ClawdNet.Core.Abstractions;

public interface IModelClientFactory
{
    IModelClient Create(ProviderDefinition provider);
}
