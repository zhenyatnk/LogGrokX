using System.Collections.ObjectModel;
using System.Linq;
using LogGrokX.Colors.Configuration;
using LogGrokX.Data;

namespace LogGrokX.Settings;

public sealed class ProfileViewModel : ViewModelBase
{
    private string _name;

    public ProfileViewModel(ProfileSettings profile)
    {
        _name = profile.Name;
        foreach (var rule in profile.ColorSettings?.Rules ?? [])
            ColorRules.Add(new ColorRuleViewModel(rule));
        foreach (var format in profile.LogFormats ?? [])
            LogFormats.Add(new LogFormatViewModel(format));
    }

    public string Name
    {
        get => _name;
        set
        {
            if (_name == value)
                return;
            _name = value;
            InvokePropertyChanged();
        }
    }

    public ObservableCollection<ColorRuleViewModel> ColorRules { get; } = new();
    public ObservableCollection<LogFormatViewModel> LogFormats { get; } = new();

    public ProfileSettings ToSettings() => new()
    {
        Name = Name.Trim(),
        InheritLegacySettings = false,
        ColorSettings = new ColorSettings
        {
            Rules = ColorRules.Select(rule => new ColorRule
            {
                RegexString = rule.RegexString,
                ForegroundColor = rule.ForegroundColor,
                BackgroundColor = rule.BackgroundColor
            }).ToArray()
        },
        LogFormats = LogFormats.Select(format =>
        {
            format.TryGetXorMask(out var mask);
            return new LogFormat
            {
                Regex = format.Regex,
                IndexedFields = format.IndexedFieldsList.ToArray(),
                TimeField = format.TimeField,
                TimeFormat = format.TimeFormat,
                Transformations = format.TransformationsList.ToArray(),
                XorMask = mask
            };
        }).ToArray()
    };
}
