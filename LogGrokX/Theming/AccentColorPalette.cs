using System;
using System.Collections.Generic;

namespace LogGrokX.Theming;

public static class AccentColorPalette
{
    public static IReadOnlyList<AccentColorDefinition> All { get; } =
    [
        new(ViewSettings.AccentColorKind.Blue, "AccentBlue", "#0078D4"),
        new(ViewSettings.AccentColorKind.Purple, "AccentPurple", "#8661C5"),
        new(ViewSettings.AccentColorKind.Pink, "AccentPink", "#E3008C"),
        new(ViewSettings.AccentColorKind.Red, "AccentRed", "#D13438"),
        new(ViewSettings.AccentColorKind.Orange, "AccentOrange", "#F7630C"),
        new(ViewSettings.AccentColorKind.Green, "AccentGreen", "#107C10"),
        new(ViewSettings.AccentColorKind.Teal, "AccentTeal", "#008272")
    ];

    public static ViewSettings.AccentColorKind Normalize(ViewSettings.AccentColorKind value) =>
        Enum.IsDefined(value) ? value : ViewSettings.AccentColorKind.Blue;

    public static string GetColor(ViewSettings.AccentColorKind value)
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
    string Color);
