// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

using System.Globalization;
using Windows.UI;
using Windows.UI.ViewManagement;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;

namespace OmniTray.Services;

internal sealed record StackTintPreset(string Name, string Hex)
{
    public string Tint => this.Name;
}

internal static class StackTintPalette
{
    private static readonly UISettings SystemSettings = new();

    public static IReadOnlyList<StackTintPreset> Presets { get; } =
    [
        new("Marigold", "#FFB900"),
        new("Orange", "#FF8C00"),
        new("Rust", "#F7630C"),
        new("Pumpkin", "#CA5010"),
        new("Burnt orange", "#DA3B01"),
        new("Salmon", "#EF6950"),
        new("Red", "#D13438"),
        new("Bright red", "#FF4343"),
        new("Rose", "#E74856"),
        new("Crimson", "#E81123"),
        new("Hot pink", "#EA005E"),
        new("Raspberry", "#C30052"),
        new("Magenta", "#E3008C"),
        new("Plum", "#BF0077"),
        new("Orchid", "#C239B3"),
        new("Purple", "#9A0089"),
        new("Azure", "#0078D4"),
        new("Dark blue", "#0063B1"),
        new("Periwinkle", "#8E8CD8"),
        new("Iris", "#6B69D6"),
        new("Lavender", "#8764B8"),
        new("Royal purple", "#744DA9"),
        new("Lilac", "#B146C2"),
        new("Grape", "#881798"),
        new("Cyan", "#0099BC"),
        new("Steel blue", "#2D7D9A"),
        new("Aqua", "#00B7C3"),
        new("Teal", "#038387"),
        new("Aquamarine", "#00B294"),
        new("Sea green", "#018574"),
        new("Emerald", "#00CC6A"),
        new("Green", "#10893E"),
        new("Taupe", "#7A7574"),
        new("Dark taupe", "#5D5A58"),
        new("Slate", "#68768A"),
        new("Blue gray", "#515C6B"),
        new("Sage", "#567C73"),
        new("Dark sage", "#486860"),
        new("Olive", "#498205"),
        new("Forest green", "#107C10"),
        new("Gray", "#767676"),
        new("Charcoal", "#4C4A48"),
        new("Cool gray", "#69797E"),
        new("Graphite", "#4A5459"),
        new("Moss", "#647C64"),
        new("Dark moss", "#525E54"),
        new("Khaki", "#847545"),
        new("Stone", "#7E735F")
    ];

    public static bool IsSystemAccent(string tint) =>
        string.Equals(tint, DropStack.SystemAccentTint, StringComparison.OrdinalIgnoreCase);

    public static bool IsNeutral(string tint) =>
        string.Equals(tint, DropStack.DefaultTint, StringComparison.OrdinalIgnoreCase);

    public static bool UsesSystemColor(string tint) => IsNeutral(tint) || IsSystemAccent(tint);

    public static Color Resolve(string tint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tint);
        if (IsNeutral(tint))
        {
            return ResolveNeutral();
        }

        if (IsSystemAccent(tint))
        {
            return SystemSettings.GetColorValue(UIColorType.Accent);
        }

        var preset = Presets.FirstOrDefault(candidate =>
            string.Equals(candidate.Tint, tint, StringComparison.OrdinalIgnoreCase));
        if (preset is not null && TryParseHexColor(preset.Hex, out var presetColor))
        {
            return presetColor;
        }

        if (TryParseHexColor(tint, out var color))
        {
            return color;
        }

        return tint.ToUpperInvariant() switch
        {
            "MINT" => ColorHelper.FromArgb(255, 16, 124, 98),
            "VIOLET" => ColorHelper.FromArgb(255, 116, 86, 210),
            _ => ColorHelper.FromArgb(255, 0, 95, 184)
        };
    }

    private static Color ResolveNeutral()
    {
        if (Application.Current.Resources.TryGetValue(
                "TextFillColorTertiaryBrush",
                out var resource) &&
            resource is SolidColorBrush brush)
        {
            return brush.Color;
        }

        return SystemSettings.GetColorValue(UIColorType.Foreground);
    }

    private static bool TryParseHexColor(string value, out Color color)
    {
        var text = value.Trim();
        if (text is ['#', _, _, _, _, _, _] &&
            uint.TryParse(text.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var rgb))
        {
            color = ColorHelper.FromArgb(
                255,
                (byte)(rgb >> 16),
                (byte)(rgb >> 8),
                (byte)rgb);
            return true;
        }

        color = default;
        return false;
    }
}
