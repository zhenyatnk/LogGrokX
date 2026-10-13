using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using LogGrokX.Data.Index;
using LogGrokX.Localization;

namespace LogGrokX.Filter
{
    public class FilterChipViewModel : ViewModelBase
    {
        private readonly int _indexedComponentIndex;
        private readonly IFilterSettings _filterSettings;

        public FilterChipViewModel(
            string fieldName,
            int indexedComponentIndex,
            IReadOnlyCollection<string> excludedValues,
            IFilterSettings filterSettings)
        {
            FieldName = fieldName;
            _indexedComponentIndex = indexedComponentIndex;
            _filterSettings = filterSettings;

            DisplayText = excludedValues.Count == 1
                ? $"{fieldName}: {excludedValues.First()}"
                : Strings.Format("HiddenValues", fieldName, excludedValues.Count);

            RemoveCommand = new DelegateCommand(
                () => _filterSettings.SetExclusions(_indexedComponentIndex, Enumerable.Empty<string>()));
        }

        public string FieldName { get; }

        public string DisplayText { get; }

        public ICommand RemoveCommand { get; }
    }
}
