using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;
using LogGrokX.Data;

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

        public string DisplayName => string.IsNullOrWhiteSpace(_name) ? "(unnamed)" : _name;

        public bool Enabled
        {
            get => _enabled;
            set
            {
                if (_enabled == value)
                    return;
                _enabled = value;
                InvokePropertyChanged();
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
                InvokePropertyChanged();
                InvokePropertyChanged(nameof(SamplePreview));
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

        public string SamplePreview
        {
            get
            {
                var data = ToData();
                data.Fields = Array.Empty<string>();
                data.Enabled = true;
                return ValueMappingText.Apply(_sampleText, new ValueMapper(new[] { data }).ForField(null));
            }
        }

        public IEnumerable<string> GetValidationErrors()
        {
            var title = DisplayName;
            if (Items.Any(item => !item.IsEmpty && string.IsNullOrWhiteSpace(item.Value)))
                yield return $"Value mapping list \"{title}\" has an item without a value.";

            var comparer = _ignoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            var duplicates = Items
                .Where(item => !string.IsNullOrWhiteSpace(item.Value))
                .GroupBy(item => item.Value.Trim(), comparer)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToList();
            if (duplicates.Count > 0)
                yield return $"Value mapping list \"{title}\" has duplicate values: {string.Join(", ", duplicates)}.";
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

            OnItemsModified();
        }

        private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e) => OnItemsModified();

        private void OnItemsModified()
        {
            InvokePropertyChanged(nameof(Items));
            InvokePropertyChanged(nameof(SamplePreview));
        }
    }
}
