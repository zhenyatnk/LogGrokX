using System;

namespace LogGrokX
{
    public class TimelinePlacementService
    {
        private readonly ApplicationSettings _applicationSettings;

        public TimelinePlacementService(ApplicationSettings applicationSettings)
        {
            _applicationSettings = applicationSettings;
            IsAtTop = applicationSettings.ViewSettings.TimelineAtTop;
            IsVisible = applicationSettings.ViewSettings.TimelineVisible;
        }

        public bool IsAtTop { get; private set; }

        public bool IsVisible { get; private set; }

        public event Action? Changed;

        public void SetAtTop(bool isAtTop) => SetPlacement(isAtTop, IsVisible);

        public void CyclePlacement()
        {
            if (!IsVisible)
                SetPlacement(true, true);
            else if (IsAtTop)
                SetPlacement(false, true);
            else
                SetPlacement(false, false);
        }

        public void SetPlacement(bool isAtTop, bool isVisible)
        {
            if (IsAtTop == isAtTop && IsVisible == isVisible)
                return;

            IsAtTop = isAtTop;
            IsVisible = isVisible;
            _applicationSettings.SetTimelineAtTop(isAtTop);
            _applicationSettings.SetTimelineVisible(isVisible);
            Changed?.Invoke();
        }
    }
}
