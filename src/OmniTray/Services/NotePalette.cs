// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using Windows.UI;

namespace OmniTray.Services;

internal static class NotePalette
{
    public static Color Resolve(NoteColor color, bool dark) => (color, dark) switch
    {
        (NoteColor.Peach, false) => Color.FromArgb(255, 255, 225, 201),
        (NoteColor.Pink, false) => Color.FromArgb(255, 250, 218, 230),
        (NoteColor.Lavender, false) => Color.FromArgb(255, 230, 222, 250),
        (NoteColor.Blue, false) => Color.FromArgb(255, 215, 235, 250),
        (NoteColor.Mint, false) => Color.FromArgb(255, 215, 241, 221),
        (_, false) => Color.FromArgb(255, 255, 243, 190),
        (NoteColor.Peach, true) => Color.FromArgb(255, 73, 51, 39),
        (NoteColor.Pink, true) => Color.FromArgb(255, 70, 43, 57),
        (NoteColor.Lavender, true) => Color.FromArgb(255, 54, 46, 72),
        (NoteColor.Blue, true) => Color.FromArgb(255, 36, 55, 73),
        (NoteColor.Mint, true) => Color.FromArgb(255, 36, 61, 47),
        _ => Color.FromArgb(255, 66, 58, 33)
    };
}
