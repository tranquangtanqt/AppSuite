using System.ComponentModel;

namespace CsvEditor.Models;

/// <summary>
/// One data row. Cells are accessed by column index through an indexer rather than a fixed set of
/// properties, because the number of columns can change at runtime (Add/Remove Column) without
/// having to change the row's CLR type. <see cref="GetCell"/>/<see cref="SetCell"/> are equivalent
/// explicit methods kept for code-behind call sites (e.g. DataGrid cell-edit handlers) where binding
/// to an indexer path is inconvenient.
/// </summary>
public sealed class CsvRow : INotifyPropertyChanged
{
    private readonly List<string> _cells;

    public CsvRow(IEnumerable<string> cells)
    {
        _cells = new List<string>(cells);
    }

    public int CellCount => _cells.Count;

    public string this[int columnIndex]
    {
        get => GetCell(columnIndex);
        set => SetCell(columnIndex, value);
    }

    public string GetCell(int columnIndex)
    {
        return columnIndex >= 0 && columnIndex < _cells.Count ? _cells[columnIndex] : string.Empty;
    }

    public void SetCell(int columnIndex, string value)
    {
        EnsureCapacity(columnIndex);
        if (_cells[columnIndex] == value)
        {
            return;
        }

        _cells[columnIndex] = value;
        OnPropertyChanged($"Item[{columnIndex}]");
    }

    /// <summary>Sets a cell without raising <see cref="PropertyChanged"/> - used by bulk column
    /// mutations running on a background thread, where the caller raises a single structural change
    /// notification afterwards instead of one event per row.</summary>
    internal void SetCellSilent(int columnIndex, string value)
    {
        EnsureCapacity(columnIndex);
        _cells[columnIndex] = value;
    }

    /// <summary>Inserts a new cell at <paramref name="columnIndex"/> (used by Add Column). Raises a
    /// whole-row refresh since every subsequent cell's index/binding shifts.</summary>
    internal void InsertCellAt(int columnIndex, string value, bool raiseEvent = true)
    {
        var index = Math.Clamp(columnIndex, 0, _cells.Count);
        _cells.Insert(index, value);
        if (raiseEvent)
        {
            OnPropertyChanged(null);
        }
    }

    /// <summary>Removes the cell at <paramref name="columnIndex"/> (used by Remove Column). Raises a
    /// whole-row refresh since every subsequent cell's index/binding shifts.</summary>
    internal void RemoveCellAt(int columnIndex, bool raiseEvent = true)
    {
        if (columnIndex < 0 || columnIndex >= _cells.Count)
        {
            return;
        }

        _cells.RemoveAt(columnIndex);
        if (raiseEvent)
        {
            OnPropertyChanged(null);
        }
    }

    public CsvRow Clone() => new(_cells);

    /// <summary>Forces bound UI to re-read every cell - used after a silent bulk mutation.</summary>
    internal void RaiseRowRefreshed() => OnPropertyChanged(null);

    private void EnsureCapacity(int columnIndex)
    {
        while (_cells.Count <= columnIndex)
        {
            _cells.Add(string.Empty);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(string? propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
