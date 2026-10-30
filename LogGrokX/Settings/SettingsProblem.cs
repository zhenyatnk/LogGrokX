namespace LogGrokX.Settings;

public enum SettingsField
{
    None,
    BigLineSize,
    MaxDumpsCount,
    ProfileName,
    LogFormatRegex,
    IndexedFields,
    Transformations,
    XorMask,
    ColorRegex,
    ForegroundColor,
    BackgroundColor,
    MappingValue
}

public sealed record SettingsProblem(
    string Message,
    SettingsField Field,
    ProfileViewModel? Profile = null,
    LogFormatViewModel? LogFormat = null,
    ColorRuleViewModel? ColorRule = null,
    ValueMappingListViewModel? ValueMapping = null,
    ValueMappingItemViewModel? MappingItem = null)
{
    public bool CanNavigate => Field != SettingsField.None;

    public override string ToString() => Message;
}
