using System;

namespace LogGrokX
{
    public class MergedFilesViewService
    {
        public bool IsEnabled { get; private set; }

        public event Action? Changed;

        public void SetEnabled(bool isEnabled)
        {
            if (IsEnabled == isEnabled)
                return;

            IsEnabled = isEnabled;
            Changed?.Invoke();
        }
    }
}
