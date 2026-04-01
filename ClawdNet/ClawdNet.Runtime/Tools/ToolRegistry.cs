using ClawdNet.Core.Abstractions;

namespace ClawdNet.Runtime.Tools;

public sealed class ToolRegistry : IToolRegistry
{
    private readonly Dictionary<string, ITool> _tools;
    private readonly object _syncRoot = new();

    public ToolRegistry(IEnumerable<ITool> tools)
    {
        _tools = tools.ToDictionary(tool => tool.Name, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<ITool> Tools
    {
        get
        {
            lock (_syncRoot)
            {
                return _tools.Values.ToArray();
            }
        }
    }

    public bool TryGet(string name, out ITool? tool)
    {
        lock (_syncRoot)
        {
            return _tools.TryGetValue(name, out tool);
        }
    }

    public void Register(ITool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);

        lock (_syncRoot)
        {
            _tools[tool.Name] = tool;
        }
    }

    public void RegisterRange(IEnumerable<ITool> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);

        lock (_syncRoot)
        {
            foreach (var tool in tools)
            {
                _tools[tool.Name] = tool;
            }
        }
    }
}
