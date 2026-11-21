using System;
using System.Collections.Generic;
using System.Linq;

namespace LogGrokX.Controls.TextRender;

public class TextViewSharedFoldingState
{
    public event EventHandler? Changed;

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

    public bool IsRootCollapsed(TextModel textModel)
    {
        if (textModel.RootFoldingLine is not int root ||
            textModel.CollapsibleRanges is not { } ranges)
            return false;

        var collapsed = this[textModel.UniqueId] ??
                        GetDefaultSettings(ranges, textModel.Count);
        return collapsed.Contains(root);
    }

    public void ToggleRoot(TextModel textModel)
    {
        if (textModel.RootFoldingLine is not int root ||
            textModel.CollapsibleRanges is not { } ranges)
            return;

        var collapsed = this[textModel.UniqueId] is { } existing
            ? new HashSet<int>(existing)
            : GetDefaultSettings(ranges, textModel.Count);
        if (!collapsed.Add(root))
            collapsed.Remove(root);

        this[textModel.UniqueId] = collapsed;
        NotifyChanged(null);
    }

    private void SetDefaultSettingsFactoryAndReset(
        Func<IEnumerable<(int start, int length)>, int, HashSet<int>> defaultSettingsFactory)
    {
        _collapsedLineIndicesByUniqueId.Clear();
        _defaultSettingsFactory = defaultSettingsFactory;
        ResetAllExistingTextViews();
        Changed?.Invoke(this, EventArgs.Empty);
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

    public void NotifyChanged(ITextFoldingClient? source)
    {
        foreach (var (_, clientRef) in _clientRefs)
        {
            if (clientRef.TryGetTarget(out var textView) && !ReferenceEquals(textView, source))
                textView.OnSharedFoldingStateChanged();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }
    
    public void Unregister(ITextFoldingClient textView)
    {
        _clientRefs.Remove(textView.GetHashCode());
    }

    private void CleanupClientRefs()
    {
        _clientRefs = _clientRefs.Where(c => c.Value.TryGetTarget(out _))
            .ToDictionary(pair => pair.Key, pair=> pair.Value);
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