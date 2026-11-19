using System;
using System.Collections.Generic;
using System.Linq;

namespace LogGrokX.Controls.ListControls;

public class ColumnSettings
{
    private readonly List<string> _hiddenColumns = new();

    public double[]? ColumnWidths { get; set; }

    public IReadOnlyList<string> HiddenColumns => _hiddenColumns;

    public event EventHandler? HiddenColumnsChanged;

    public bool IsColumnHidden(string fieldName) =>
        _hiddenColumns.Any(name => string.Equals(name, fieldName, StringComparison.OrdinalIgnoreCase));

    public void SetColumnHidden(string fieldName, bool hidden)
    {
        var index = _hiddenColumns.FindIndex(name =>
            string.Equals(name, fieldName, StringComparison.OrdinalIgnoreCase));
        if (hidden)
        {
            if (index >= 0)
                return;
            _hiddenColumns.Add(fieldName);
        }
        else
        {
            if (index < 0)
                return;
            _hiddenColumns.RemoveAt(index);
        }

        HiddenColumnsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ShowAllColumns()
    {
        if (_hiddenColumns.Count == 0)
            return;

        _hiddenColumns.Clear();
        HiddenColumnsChanged?.Invoke(this, EventArgs.Empty);
    }
}
