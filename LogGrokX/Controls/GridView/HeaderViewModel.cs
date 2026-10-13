using LogGrokX.Filter;

namespace LogGrokX.Controls.GridView
{
    internal class HeaderViewModel
    {
        public HeaderViewModel(string? fieldHeader, FilterViewModel? filterViewModel)
        {
            FieldHeader = fieldHeader;
            FilterViewModel = filterViewModel;
        }
        
        public string? FieldHeader { get; }
        public FilterViewModel? FilterViewModel { get; }

        public bool IsFilteredField => FilterViewModel != null;
    }
}
