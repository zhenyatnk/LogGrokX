using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace LogGrokX.Theming;

public static class AccentColorPalette
{
    public static IReadOnlyList<AccentColorDefinition> All { get; } =
    [
        new(ViewSettings.AccentColorKind.Blue, "AccentBlue", Color.FromRgb(0x00, 0x78, 0xD4)),
        new(ViewSettings.AccentColorKind.Purple, "AccentPurple", Color.FromRgb(0x86, 0x61, 0xC5)),
        new(ViewSettings.AccentColorKind.Pink, "AccentPink", Color.FromRgb(0xE3, 0x00, 0x8C)),
        new(ViewSettings.AccentColorKind.Red, "AccentRed", Color.FromRgb(0xD1, 0x34, 0x38)),
        new(ViewSettings.AccentColorKind.Orange, "AccentOrange", Color.FromRgb(0xF7, 0x63, 0x0C)),
        new(ViewSettings.AccentColorKind.Green, "AccentGreen", Color.FromRgb(0x10, 0x7C, 0x10)),
        new(ViewSettings.AccentColorKind.Teal, "AccentTeal", Color.FromRgb(0x00, 0x82, 0x72))
    ];

    public static ViewSettings.AccentColorKind Normalize(ViewSettings.AccentColorKind value) =>
        Enum.IsDefined(value) ? value : ViewSettings.AccentColorKind.Blue;

    public static Color GetColor(ViewSettings.AccentColorKind value)
    {
        value = Normalize(value);
        foreach (var definition in All)
        {
            if (definition.Value == value)
                return definition.Color;
        }

        return All[0].Color;
    }
}

public sealed record AccentColorDefinition(
    ViewSettings.AccentColorKind Value,
    string TitleResource,
    Color Color);
