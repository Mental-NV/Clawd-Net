using ClawdNet.Core.Abstractions;

namespace ClawdNet.Runtime.Tools;

public sealed class ToolRegistry : IToolRegistry
{
    private readonly Dictionary<string, ITool> _tools;

    public ToolRegistry(IEnumerable<ITool> tools)
    {
        _tools = tools.ToDictionary(tool => tool.Name, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<ITool> Tools => _tools.Values.ToArray();

    public bool TryGet(string name, out ITool? tool) => _tools.TryGetValue(name, out tool);
}
