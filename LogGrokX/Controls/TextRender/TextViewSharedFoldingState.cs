using System;
using System.Collections.Generic;
using System.Linq;

namespace LogGrokX.Controls.TextRender;

public class TextViewSharedFoldingState
{
    private int _cleanupCounter = 0;

    private Func<IEnumerable<(int start, int length)>, int, HashSet<int>> _defaultSettingsFactory = CreateDefaultSettings;
    private Dictionary<int, WeakReference<ITextFoldingClient>> _clientRefs = new();

    public HashSet<int>? this[int id]
    {
        get => _collapsedLineIndicesByUniqueId.TryGetValue(id, out var result) ?
            result : null;
        set => _collapsedLineIndicesByUniqueId[id] = value;
    }

    public void CollapseAll() => SetDefaultSettingsFactoryAndReset(CreateSettingsForAllCollapsed);

    public void ExpandAll() => SetDefaultSettingsFactoryAndReset(CreateSettingsForAllExpanded);

    public void ResetAllToDefault() => SetDefaultSettingsFactoryAndReset(CreateDefaultSettings);

    public HashSet<int> GetDefaultSettings(
        IEnumerable<(int start, int length)> collapsibleRanges,
        int totalLineCount)
    {
        return _defaultSettingsFactory(collapsibleRanges, totalLineCount);
    }

    public HashSet<int> GetDefaultFoldingSettings(IEnumerable<(int start, int length)> collapsibleRanges,
        int totalLineCount)
    {
        return CreateDefaultSettings(collapsibleRanges, totalLineCount);
    }

    private void SetDefaultSettingsFactoryAndReset(
        Func<IEnumerable<(int start, int length)>, int, HashSet<int>> defaultSettingsFactory)
    {
        _collapsedLineIndicesByUniqueId.Clear();
        _defaultSettingsFactory = defaultSettingsFactory;
        ResetAllExistingTextViews();
    }

    private void ResetAllExistingTextViews()
    {
        foreach (var (_, clientRef) in _clientRefs)
        {
            if (clientRef.TryGetTarget(out var textView))
                textView.ResetFoldingToDefault();
        }
    }

    public void Register(ITextFoldingClient textView)
    {
        _clientRefs[textView.GetHashCode()] = new WeakReference<ITextFoldingClient>(textView);
        _cleanupCounter++;
        if (_cleanupCounter % 256 == 0)
        {
            CleanupClientRefs();
        }
    }

    public void NotifyChanged(ITextFoldingClient source)
    {
        foreach (var (_, clientRef) in _clientRefs)
        {
            if (clientRef.TryGetTarget(out var textView) && !ReferenceEquals(textView, source))
                textView.OnSharedFoldingStateChanged();
        }
    }

    public void Unregister(ITextFoldingClient textView)
    {
        _clientRefs.Remove(textView.GetHashCode());
    }

    private void CleanupClientRefs()
    {
        _clientRefs = _clientRefs.Where(c => c.Value.TryGetTarget(out _))
            .ToDictionary(pair => pair.Key, pair => pair.Value);
    }

    private static HashSet<int> CreateSettingsForAllExpanded(IEnumerable<(int start, int length)> collapsibleRanges,
        int totalLineCount)
    {
        return new HashSet<int>();
    }

    private static HashSet<int> CreateSettingsForAllCollapsed(IEnumerable<(int start, int length)> collapsibleRanges,
        int totalLineCount)
    {
        return new HashSet<int>(collapsibleRanges.Select(c => c.start));
    }

    private static HashSet<int> CreateDefaultSettings(
        IEnumerable<(int start, int length)> collapsibleRanges, int totalLineCount)
    {
        return CreateSettingsForAllCollapsed(collapsibleRanges, totalLineCount);
    }

    private readonly Dictionary<int, HashSet<int>?> _collapsedLineIndicesByUniqueId = new();
}
