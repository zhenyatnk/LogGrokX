using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using LogGrokX.Platform;
using LogGrokX.Data;
using LogGrokX.Data.Index;

namespace LogGrokX.Filter
{
    public class FilterViewModel : ViewModelBase, IDisposable
    {
        private readonly IFilterSettings _filterSettings;
        private readonly IComponentIndexer _indexer;
        private string? _textFilter;
        private readonly int _indexedFieldIndex;
        private readonly IUiDispatcher _dispatcher;
        private readonly HashSet<string> _knownComponents = new(StringComparer.Ordinal);
        private readonly object _newComponentsSync = new();
        private bool _disposed;

        public FilterViewModel(
            string fieldName,
            IFilterSettings filterSettings,
            IComponentIndexer indexer,
            LogMetaInformation metaInformation,
            IUiDispatcher dispatcher)
        {
            _dispatcher = dispatcher;
            if (!_dispatcher.CheckAccess()) throw new InvalidOperationException("UI thread access is required.");
            _filterSettings = filterSettings;
            _filterSettings.ExclusionsChanged += OnExclusionsChanged;
            
            _indexer = indexer;
            _indexedFieldIndex = metaInformation.GetIndexedFieldIndexByName(fieldName);
            _indexer.ComponentAdded += OnNewComponentAdded;

            var fieldValues =
                _indexer.GetAllComponents(_indexedFieldIndex);


            Elements = new ObservableCollection<ElementViewModel>(
                fieldValues.Where(_knownComponents.Add).Select(CreateElementViewModel));
        }

        private readonly ConcurrentBag<(int componentNumber, string value)> _newComponentsQueue = new();
        private IDisposable? _pendingComponentUpdate;

        public void Dispose()
        {
            if (!_dispatcher.CheckAccess()) throw new InvalidOperationException("UI thread access is required.");
            lock (_newComponentsSync)
            {
                if (_disposed)
                    return;
                _disposed = true;
                _indexer.ComponentAdded -= OnNewComponentAdded;
                _filterSettings.ExclusionsChanged -= OnExclusionsChanged;
                _pendingComponentUpdate?.Dispose();
                _pendingComponentUpdate = null;
                _newComponentsQueue.Clear();
            }
            foreach (var element in Elements)
                element.Dispose();
        }

        private void OnExclusionsChanged() => InvokePropertyChanged(nameof(IsFilterApplied));

        private ElementViewModel CreateElementViewModel(string fieldValue)
        {
            var newElementViewModel =
                new ElementViewModel(
                    fieldValue,
                    _indexedFieldIndex,
                    _filterSettings,
                    () => _indexer.GetIndexCountForComponent(_indexedFieldIndex, fieldValue));

            return newElementViewModel;
        }

        private void OnNewComponentAdded(int componentIndex, string value)
        {
            if (componentIndex != _indexedFieldIndex)
            {
                return;
            }

            lock (_newComponentsSync)
            {
                if (_disposed)
                    return;
                _newComponentsQueue.Add((componentIndex, value));
                _pendingComponentUpdate ??= _dispatcher.Post(ProcessNewComponents);
            }
        }

        private void ProcessNewComponents()
        {
            lock (_newComponentsSync)
            {
                _pendingComponentUpdate = null;
                if (_disposed)
                    return;
                while (_newComponentsQueue.TryTake(out var valueTuple))
                {
                    var (_, componentValue) = valueTuple;
                    if (!_knownComponents.Add(componentValue))
                        continue;
                    Elements.Add(CreateElementViewModel(componentValue));
                }
            }
        }

        public string? TextFilter
        {
            get => _textFilter;
            set => SetAndRaiseIfChanged(ref _textFilter, value);
        }

        public ObservableCollection<ElementViewModel> Elements { get; }

        public bool IsFilterApplied => 
            _filterSettings.Exclusions.TryGetValue(_indexedFieldIndex, out var exclusionList) 
                && exclusionList.Any();

        public DelegateCommand DeselectAllCommand =>
            new(() => _filterSettings.ExcludeAllExcept(_indexedFieldIndex,
                Enumerable.Empty<string>()));

        public DelegateCommand SelectAllCommand =>
            new(() => _filterSettings.SetExclusions(_indexedFieldIndex,
                Enumerable.Empty<string>()));

        public DelegateCommand SelectOnlySearchResultsCommand =>
            DelegateCommand.Create<IEnumerable>(SelectOnlySearchResults);

        private void SelectOnlySearchResults(IEnumerable items)
        {
            _filterSettings.ExcludeAllExcept(_indexedFieldIndex,
                items.Cast<ElementViewModel>().Select(item => item.Name));
        }
    }
}
