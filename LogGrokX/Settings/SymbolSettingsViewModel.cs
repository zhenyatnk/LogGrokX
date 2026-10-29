using LogGrokX.Symbols;

namespace LogGrokX.Settings
{
    public sealed class SymbolSettingsViewModel : ViewModelBase
    {
        private bool _enabled;
        private string _symbolPath;
        private string _sourcePath;
        private string _cachePath;
        private bool _allowSourceServerCommands;

        public SymbolSettingsViewModel(SymbolSettings settings)
        {
            _enabled = settings.Enabled;
            _symbolPath = settings.SymbolPath ?? string.Empty;
            _sourcePath = settings.SourcePath ?? string.Empty;
            _cachePath = settings.CachePath ?? string.Empty;
            _allowSourceServerCommands = settings.AllowSourceServerCommands;
        }

        public bool Enabled
        {
            get => _enabled;
            set => SetAndRaiseIfChanged(ref _enabled, value);
        }

        public string SymbolPath
        {
            get => _symbolPath;
            set => SetAndRaiseIfChanged(ref _symbolPath, value);
        }

        public string SourcePath
        {
            get => _sourcePath;
            set => SetAndRaiseIfChanged(ref _sourcePath, value);
        }

        public string CachePath
        {
            get => _cachePath;
            set => SetAndRaiseIfChanged(ref _cachePath, value);
        }

        public bool AllowSourceServerCommands
        {
            get => _allowSourceServerCommands;
            set => SetAndRaiseIfChanged(ref _allowSourceServerCommands, value);
        }

        public SymbolSettings ToSettings() => new()
        {
            Enabled = Enabled,
            SymbolPath = SymbolPath.Trim(),
            SourcePath = SourcePath.Trim(),
            CachePath = CachePath.Trim(),
            AllowSourceServerCommands = AllowSourceServerCommands
        };
    }
}
