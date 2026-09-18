namespace CodexInputEnhancer.Services;

public sealed class EditHistory
{
    private readonly List<string> _items = [];
    private int _index = -1;

    public bool CanUndo => _index > 0;

    public void Reset()
    {
        _items.Clear();
        _index = -1;
    }

    public void CommitOptimization(string currentText, string optimizedText)
    {
        if (_index < 0)
        {
            _items.Add(currentText);
            _index = 0;
        }
        else
        {
            TrimForward();
            if (!string.Equals(_items[_index], currentText, StringComparison.Ordinal))
            {
                _items.Add(currentText);
                _index = _items.Count - 1;
            }
        }

        TrimForward();
        if (string.Equals(_items[_index], optimizedText, StringComparison.Ordinal)) return;
        _items.Add(optimizedText);
        _index = _items.Count - 1;
    }

    public string? Undo()
    {
        if (!CanUndo) return null;
        _index--;
        return _items[_index];
    }

    private void TrimForward()
    {
        if (_index >= 0 && _index < _items.Count - 1)
            _items.RemoveRange(_index + 1, _items.Count - _index - 1);
    }

}
