// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

namespace OmniTray.Core;

public static class EdgeContentSharingPolicy
{
    public static EdgeShelfSide ResolveContentSource(
        EdgeShelfSide side,
        bool syncLeftAndRight,
        bool syncTopAndBottom,
        bool syncAll)
    {
        if (!Enum.IsDefined(side))
        {
            throw new ArgumentOutOfRangeException(nameof(side));
        }

        if (syncAll)
        {
            return EdgeShelfSide.Left;
        }

        return side switch
        {
            EdgeShelfSide.Right when syncLeftAndRight => EdgeShelfSide.Left,
            EdgeShelfSide.Bottom when syncTopAndBottom => EdgeShelfSide.Top,
            _ => side
        };
    }
}
