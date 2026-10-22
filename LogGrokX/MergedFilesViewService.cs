using System;

namespace LogGrokX
{
    public class MergedFilesViewService
    {
        private readonly ApplicationSettings _applicationSettings;

        public MergedFilesViewService(ApplicationSettings applicationSettings)
        {
            _applicationSettings = applicationSettings;
            applicationSettings.SetMergedFilesView(false);
        }

        public bool IsEnabled { get; private set; }

        public event Action? Changed;

        public void SetEnabled(bool isEnabled)
        {
            if (IsEnabled == isEnabled)
                return;

            IsEnabled = isEnabled;
            _applicationSettings.SetMergedFilesView(isEnabled);
            Changed?.Invoke();
        }
    }
}
