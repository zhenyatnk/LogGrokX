using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace LogGrokX.Search
{
    public class SavedSearchPattern : INotifyPropertyChanged
    {
        private string _name = string.Empty;
        private string _pattern = string.Empty;
        private bool _isCaseSensitive;
        private bool _useRegex;

        public string Name
        {
            get => _name;
            set
            {
                if (_name == value) return;
                _name = value;
                InvokePropertyChanged();
            }
        }

        public string Pattern
        {
            get => _pattern;
            set
            {
                if (_pattern == value) return;
                _pattern = value;
                InvokePropertyChanged();
            }
        }

        public bool IsCaseSensitive
        {
            get => _isCaseSensitive;
            set
            {
                if (_isCaseSensitive == value) return;
                _isCaseSensitive = value;
                InvokePropertyChanged();
            }
        }

        public bool UseRegex
        {
            get => _useRegex;
            set
            {
                if (_useRegex == value) return;
                _useRegex = value;
                InvokePropertyChanged();
            }
        }

        [JsonIgnore]
        public bool IsEditing { get; private set; }

        [JsonIgnore]
        public string EditName { get; set; } = string.Empty;

        [JsonIgnore]
        public string EditPattern { get; set; } = string.Empty;

        public event PropertyChangedEventHandler? PropertyChanged;

        public SearchPattern ToSearchPattern() => new(Pattern, IsCaseSensitive, UseRegex);

        public static SavedSearchPattern FromSearchPattern(string name, in SearchPattern searchPattern) =>
            new()
            {
                Name = name,
                Pattern = searchPattern.Pattern,
                IsCaseSensitive = searchPattern.IsCaseSensitive,
                UseRegex = searchPattern.UseRegex
            };

        public bool MatchesFilter(string filter) =>
            string.IsNullOrWhiteSpace(filter) ||
            Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
            Pattern.Contains(filter, StringComparison.OrdinalIgnoreCase);

        public void BeginEdit()
        {
            EditName = Name;
            EditPattern = Pattern;
            IsEditing = true;
            InvokePropertyChanged(nameof(IsEditing));
            InvokePropertyChanged(nameof(EditName));
            InvokePropertyChanged(nameof(EditPattern));
        }

        public void CancelEdit()
        {
            IsEditing = false;
            InvokePropertyChanged(nameof(IsEditing));
        }

        public void CommitEdit()
        {
            Name = string.IsNullOrWhiteSpace(EditName) ? EditPattern : EditName.Trim();
            Pattern = EditPattern;
            IsEditing = false;
            InvokePropertyChanged(nameof(IsEditing));
        }

        private void InvokePropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
