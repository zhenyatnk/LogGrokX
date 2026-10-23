using System;
using System.Threading.Tasks;

namespace LogGrokX.Filter
{
    public class ElementViewModel : ViewModelBase, IDisposable
    {
        public string Name { get; }

        public bool IsActive
        {
            get => _filterSettings[(_componentIndex, Name)];
            set
            {
                if (_filterSettings[(_componentIndex, Name)] == value) return;
                _filterSettings[(_componentIndex, Name)] = value;
                InvokePropertyChanged();
            } 
        }

        public int Percent
        {
            get
            {
                var now = DateTime.Now;
                if (now - _percentCacheTime > TimeSpan.FromSeconds(3))
                {
                    _percentCacheTime = now;
                    UpdateCachedPercent(now);
                }

                return _cachedPercent;
            }
        }

        async void UpdateCachedPercent(DateTime timeStamp)
        {
            _cachedPercent = await Task.Factory.StartNew(() => _percentGetter());
            InvokePropertyChanged(nameof(Percent));
        }

        private readonly Func<int> _percentGetter;
        private readonly IFilterSettings _filterSettings;
        private readonly int _componentIndex;
        
        private int _cachedPercent;
        private DateTime _percentCacheTime = DateTime.MinValue;
        
        public ElementViewModel(
            string name,
            int componentIndex, 
            IFilterSettings filterSettings, 
            Func<int> percentGetter)
        {
            Name = name;

            _componentIndex = componentIndex;
            _filterSettings = filterSettings;
            _filterSettings.ExclusionsChanged += OnExclusionsChanged;
            _percentGetter = percentGetter;
        }

        public void Dispose() => _filterSettings.ExclusionsChanged -= OnExclusionsChanged;

        private void OnExclusionsChanged() => InvokePropertyChanged(nameof(IsActive));
    }
}
