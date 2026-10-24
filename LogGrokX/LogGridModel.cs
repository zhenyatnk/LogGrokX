using System;
using System.Collections.Generic;
using LogGrokX.Data;
using LogGrokX.Filter;
using LogGrokX.Controls.ListControls;

namespace LogGrokX;

public sealed class LogGridModel(LogMetaInformation metaInformation, ColumnSettings columnSettings,
    Func<string, FilterViewModel>? filterFactory = null, bool hasSourceColumn = false) : IDisposable
{
    private readonly Dictionary<string, FilterViewModel> _filters = new(StringComparer.Ordinal);
    private bool _disposed;

    public LogMetaInformation MetaInformation { get; } = metaInformation;
    public ColumnSettings ColumnSettings { get; } = columnSettings;
    public bool HasSourceColumn { get; } = hasSourceColumn;

    public FilterViewModel? GetFilter(string field)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (filterFactory == null || !MetaInformation.IsFieldIndexed(field)) return null;
        if (!_filters.TryGetValue(field, out var filter))
            _filters.Add(field, filter = filterFactory(field));
        return filter;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var filter in _filters.Values) filter.Dispose();
        _filters.Clear();
    }
}
