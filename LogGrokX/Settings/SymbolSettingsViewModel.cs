using System;
using System.Linq;
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
            _symbolPath = ToLines(settings.SymbolPath);
            _sourcePath = ToLines(settings.SourcePath);
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

        // The settings store WinDbg-style ';'-separated paths, the editor shows one element per line.
        internal static string ToLines(string? path) => string.Join(Environment.NewLine, Split(path));

        internal static string FromLines(string? lines) => string.Join(";", Split(lines));

        private static string[] Split(string? path) =>
            (path ?? string.Empty).Split(Data.Symbols.SymbolPath.Separators)
                .Select(element => element.Trim())
                .Where(element => element.Length > 0)
                .ToArray();

        public SymbolSettings ToSettings() => new()
        {
            Enabled = Enabled,
            SymbolPath = FromLines(SymbolPath),
            SourcePath = FromLines(SourcePath),
            CachePath = CachePath.Trim(),
            AllowSourceServerCommands = AllowSourceServerCommands
        };
    }
}
