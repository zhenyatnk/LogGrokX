using System;

namespace LogGrokX
{
    public class ThreadGroupingService
    {
        private readonly ApplicationSettings _applicationSettings;

        public ThreadGroupingService(ApplicationSettings applicationSettings)
        {
            _applicationSettings = applicationSettings;
            IsEnabled = applicationSettings.ViewSettings.GroupByThread;
            ShowSeparators = applicationSettings.ViewSettings.ShowThreadGroupSeparators;
        }

        public bool IsEnabled { get; private set; }

        public bool ShowSeparators { get; private set; }

        public event Action? Changed;

        public void SetEnabled(bool isEnabled)
        {
            if (IsEnabled == isEnabled)
                return;

            IsEnabled = isEnabled;
            _applicationSettings.SetGroupByThread(isEnabled);
            Changed?.Invoke();
        }

        public void SetShowSeparators(bool showSeparators)
        {
            if (ShowSeparators == showSeparators)
                return;

            ShowSeparators = showSeparators;
            _applicationSettings.SetShowThreadGroupSeparators(showSeparators);
            Changed?.Invoke();
        }
    }
}
