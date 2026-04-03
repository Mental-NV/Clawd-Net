namespace ClawdNet.Terminal.Models;

public sealed class PromptHistoryBuffer
{
    private readonly List<string> _entries = [];
    private readonly int _maxEntries;
    private int? _index;
    private string _draft = string.Empty;

    public PromptHistoryBuffer(int maxEntries = 50)
    {
        _maxEntries = maxEntries;
    }

    public void Add(string entry)
    {
        if (string.IsNullOrWhiteSpace(entry))
        {
            ResetNavigation();
            return;
        }

        if (_entries.Count == 0 || !string.Equals(_entries[^1], entry, StringComparison.Ordinal))
        {
            _entries.Add(entry);
            if (_entries.Count > _maxEntries)
            {
                _entries.RemoveAt(0);
            }
        }

        ResetNavigation();
    }

    public string Previous(string currentBuffer)
    {
        if (_entries.Count == 0)
        {
            return currentBuffer;
        }

        if (_index is null)
        {
            _draft = currentBuffer;
            _index = _entries.Count - 1;
        }
        else if (_index > 0)
        {
            _index--;
        }

        return _entries[_index.Value];
    }

    public string Next()
    {
        if (_index is null)
        {
            return _draft;
        }

        if (_index < _entries.Count - 1)
        {
            _index++;
            return _entries[_index.Value];
        }

        var result = _draft;
        ResetNavigation();
        return result;
    }

    public void ResetNavigation()
    {
        _index = null;
        _draft = string.Empty;
    }
}
