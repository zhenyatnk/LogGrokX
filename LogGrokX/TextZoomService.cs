using System;
using LogGrokX.Platform;

namespace LogGrokX
{
    public class TextZoomService
    {
        public const string FontSizeResourceKey = "LogFontSize";
        public const double DefaultFontSize = 12;
        public const double MinFontSize = 6;
        public const double MaxFontSize = 48;
        public const double Step = 1;

        private readonly ApplicationSettings _applicationSettings;
        private readonly ITextSizePresenter? _presenter;

        public TextZoomService(ApplicationSettings applicationSettings, ITextSizePresenter? presenter = null)
        {
            _applicationSettings = applicationSettings;
            _presenter = presenter;
            FontSize = Clamp(applicationSettings.ViewSettings.LogFontSize);
            Apply();
        }

        public double FontSize { get; private set; }

        public event Action? Changed;

        public bool CanIncrease => FontSize < MaxFontSize;

        public bool CanDecrease => FontSize > MinFontSize;

        public void Increase() => SetFontSize(FontSize + Step);

        public void Decrease() => SetFontSize(FontSize - Step);

        public void Reset() => SetFontSize(DefaultFontSize);

        public void SetFontSize(double fontSize)
        {
            fontSize = Clamp(fontSize);
            if (Math.Abs(FontSize - fontSize) < 0.001)
                return;

            FontSize = fontSize;
            _applicationSettings.SetLogFontSize(fontSize);
            Apply();
            Changed?.Invoke();
        }

        private void Apply() => _presenter?.ApplyFontSize(FontSize);

        private static double Clamp(double fontSize)
        {
            if (double.IsNaN(fontSize) || fontSize <= 0)
                return DefaultFontSize;

            return Math.Min(MaxFontSize, Math.Max(MinFontSize, fontSize));
        }
    }
}