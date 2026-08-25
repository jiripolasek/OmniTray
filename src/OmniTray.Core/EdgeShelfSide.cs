// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

namespace OmniTray.Core;

public enum EdgeShelfSide
{
    Left,
    Right,
    Top,
    Bottom
}

public static class EdgeShelfSideExtensions
{
    public static bool IsVertical(this EdgeShelfSide side) =>
        side is EdgeShelfSide.Left or EdgeShelfSide.Right;

    public static string GetDisplayName(this EdgeShelfSide side) => side switch
    {
        EdgeShelfSide.Left => "Left",
        EdgeShelfSide.Right => "Right",
        EdgeShelfSide.Top => "Top",
        EdgeShelfSide.Bottom => "Bottom",
        _ => throw new ArgumentOutOfRangeException(nameof(side))
    };
}
