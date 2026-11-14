using System;

namespace LogGrokX
{
    public class UpdateIndicatorService
    {
        private readonly ApplicationSettings _applicationSettings;

        public UpdateIndicatorService(ApplicationSettings applicationSettings)
        {
            _applicationSettings = applicationSettings;
            IsVisible = applicationSettings.ViewSettings.ShowUpdateIndicator;
        }

        public bool IsVisible { get; private set; }

        public event Action? Changed;

        public void SetVisible(bool isVisible)
        {
            if (IsVisible == isVisible)
                return;

            IsVisible = isVisible;
            _applicationSettings.SetShowUpdateIndicator(isVisible);
            Changed?.Invoke();
        }
    }
}
