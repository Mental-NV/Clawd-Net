namespace ClawdNet.Core.Abstractions;

public interface IToolRegistry
{
    IReadOnlyCollection<ITool> Tools { get; }

    bool TryGet(string name, out ITool? tool);
}
