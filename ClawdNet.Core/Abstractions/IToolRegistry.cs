namespace ClawdNet.Core.Abstractions;

public interface IToolRegistry
{
    IReadOnlyCollection<ITool> Tools { get; }

    bool TryGet(string name, out ITool? tool);

    void Register(ITool tool);

    void RegisterRange(IEnumerable<ITool> tools);

    void UnregisterWhere(Func<ITool, bool> predicate);
}
