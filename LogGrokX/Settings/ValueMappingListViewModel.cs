using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;
using LogGrokX.Data;
using LogGrokX.Localization;

namespace LogGrokX.Settings
{
    public sealed class ValueMappingItemViewModel : ViewModelBase
    {
        private string _value = string.Empty;
        private string _mapping = string.Empty;

        public ValueMappingItemViewModel()
        {
        }

        public ValueMappingItemViewModel(ValueMappingItem item)
        {
            _value = item.Value;
            _mapping = item.Mapping;
        }

        public string Value
        {
            get => _value;
            set
            {
                if (_value == value)
                    return;
                _value = value ?? string.Empty;
                InvokePropertyChanged();
            }
        }

        public string Mapping
        {
            get => _mapping;
            set
            {
                if (_mapping == value)
                    return;
                _mapping = value ?? string.Empty;
                InvokePropertyChanged();
            }
        }

        public bool IsEmpty => string.IsNullOrWhiteSpace(_value) && string.IsNullOrWhiteSpace(_mapping);

        public ValueMappingItem ToData() => new() { Value = _value.Trim(), Mapping = _mapping };
    }

    public sealed class ValueMappingListViewModel : ViewModelBase
    {
        private string _name = string.Empty;
        private bool _enabled = true;
        private bool _ignoreCase;
        private string _fields = string.Empty;
        private string _sampleText = string.Empty;
        private ValueMappingItemViewModel? _selectedItem;
        private Func<IEnumerable<ValueMappingListViewModel>>? _previewLists;
        private FieldValueMapper? _previewMapper;

        public ValueMappingListViewModel()
            : this(new ValueMappingList())
        {
        }

        public ValueMappingListViewModel(ValueMappingList list)
        {
            _name = list.Name;
            _enabled = list.Enabled;
            _ignoreCase = list.IgnoreCase;
            _fields = string.Join(", ", list.Fields);

            foreach (var item in list.Items)
                Items.Add(new ValueMappingItemViewModel(item));
            foreach (var item in Items)
                item.PropertyChanged += OnItemPropertyChanged;
            _sampleText = Items.FirstOrDefault(item => !string.IsNullOrWhiteSpace(item.Value))?.Value ?? string.Empty;

            Items.CollectionChanged += OnItemsChanged;
            AddItemCommand = new DelegateCommand(() =>
            {
                var item = new ValueMappingItemViewModel();
                Items.Add(item);
                SelectedItem = item;
            });
            RemoveItemCommand = new DelegateCommand(() =>
            {
                if (_selectedItem != null)
                    Items.Remove(_selectedItem);
            });
        }

        public ObservableCollection<ValueMappingItemViewModel> Items { get; } = new();

        public ICommand AddItemCommand { get; }

        public ICommand RemoveItemCommand { get; }

        public string Name
        {
            get => _name;
            set
            {
                if (_name == value)
                    return;
                _name = value ?? string.Empty;
                InvokePropertyChanged();
                InvokePropertyChanged(nameof(DisplayName));
            }
        }

        public string DisplayName => string.IsNullOrWhiteSpace(_name) ? Strings.Get("Unnamed") : _name;

        public bool Enabled
        {
            get => _enabled;
            set
            {
                if (_enabled == value)
                    return;
                _enabled = value;
                OnPreviewConfigurationChanged(nameof(Enabled));
            }
        }

        public bool IgnoreCase
        {
            get => _ignoreCase;
            set
            {
                if (_ignoreCase == value)
                    return;
                _ignoreCase = value;
                OnPreviewConfigurationChanged(nameof(IgnoreCase));
            }
        }

        public string Fields
        {
            get => _fields;
            set
            {
                if (_fields == value)
                    return;
                _fields = value ?? string.Empty;
                InvokePropertyChanged();
            }
        }

        public IReadOnlyList<string> FieldsList =>
            _fields.Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

        public ValueMappingItemViewModel? SelectedItem
        {
            get => _selectedItem;
            set
            {
                if (ReferenceEquals(_selectedItem, value))
                    return;
                _selectedItem = value;
                InvokePropertyChanged();
            }
        }

        public string SampleText
        {
            get => _sampleText;
            set
            {
                if (_sampleText == value)
                    return;
                _sampleText = value ?? string.Empty;
                InvokePropertyChanged();
                InvokePropertyChanged(nameof(SamplePreview));
            }
        }

        public Func<IEnumerable<ValueMappingListViewModel>>? PreviewLists
        {
            get => _previewLists;
            set
            {
                _previewLists = value;
                InvalidatePreview();
            }
        }

        public string SamplePreview =>
            ValueMappingText.Apply(_sampleText, _previewMapper ??= CreatePreviewMapper());

        internal void InvalidatePreview() => _previewMapper = null;

        public void RefreshPreview() => InvokePropertyChanged(nameof(SamplePreview));

        private FieldValueMapper CreatePreviewMapper()
        {
            var lists = PreviewLists?.Invoke() ?? new[] { this };
            var data = lists
                .Where(list => list.Enabled || ReferenceEquals(list, this))
                .Select(list =>
                {
                    var item = list.ToData();
                    item.Fields = Array.Empty<string>();
                    item.Enabled = true;
                    return item;
                });
            return new ValueMapper(data).ForField(null);
        }

        public IEnumerable<string> GetValidationErrors() => GetValidationProblems().Select(problem => problem.Message);

        public IEnumerable<SettingsProblem> GetValidationProblems()
        {
            var title = DisplayName;
            var missing = Items.FirstOrDefault(item => string.IsNullOrWhiteSpace(item.Value));
            if (missing != null)
                yield return new(Strings.Format("MappingValueMissing", title), SettingsField.MappingValue,
                    ValueMapping: this, MappingItem: missing);

            var comparer = _ignoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            var duplicates = Items
                .Where(item => !string.IsNullOrWhiteSpace(item.Value))
                .GroupBy(item => item.Value.Trim(), comparer)
                .Where(group => group.Count() > 1)
                .ToList();
            if (duplicates.Count > 0)
                yield return new(Strings.Format("MappingDuplicates", title, string.Join(", ", duplicates.Select(group => group.Key))),
                    SettingsField.MappingValue, ValueMapping: this, MappingItem: duplicates[0].ElementAt(1));
        }

        public ValueMappingList ToData() => new()
        {
            Name = _name.Trim(),
            Enabled = _enabled,
            IgnoreCase = _ignoreCase,
            Fields = FieldsList.ToArray(),
            Items = Items.Where(item => !item.IsEmpty).Select(item => item.ToData()).ToArray()
        };

        private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (var item in e.OldItems.OfType<ValueMappingItemViewModel>())
                    item.PropertyChanged -= OnItemPropertyChanged;
            }

            if (e.NewItems != null)
            {
                foreach (var item in e.NewItems.OfType<ValueMappingItemViewModel>())
                    item.PropertyChanged += OnItemPropertyChanged;
            }

            OnPreviewConfigurationChanged(nameof(Items));
        }

        private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e) =>
            OnPreviewConfigurationChanged(nameof(Items));

        private void OnPreviewConfigurationChanged(string propertyName)
        {
            InvalidatePreview();
            InvokePropertyChanged(propertyName);
            RefreshPreview();
        }
    }
}
