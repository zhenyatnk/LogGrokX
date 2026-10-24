using System;
using System.Windows.Media;

namespace LogGrokX.Platform;

public sealed class WpfColorValidator : IColorValidator
{
    public bool IsValid(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return true;
        try { return ColorConverter.ConvertFromString(value) is Color; }
        catch (FormatException) { return false; }
    }
}
