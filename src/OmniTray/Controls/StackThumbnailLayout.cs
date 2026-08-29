// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

namespace OmniTray.Controls;

internal static class StackThumbnailLayout
{
    private const int CompactColumnCount = 3;
    private const double CompactMinimumItemWidth = 96;

    internal static double GetItemWidth(double availableWidth, double preferredItemWidth)
    {
        if (!double.IsFinite(availableWidth) || availableWidth <= 0)
        {
            return 0;
        }

        // Resizable hosts keep tiles at the requested size and let ItemsWrapGrid wrap them.
        // Compact hosts leave the preferred width unset to retain their three-column layout.
        return double.IsFinite(preferredItemWidth) && preferredItemWidth > 0
            ? Math.Min(preferredItemWidth, availableWidth)
            : Math.Max(CompactMinimumItemWidth, Math.Floor(availableWidth / CompactColumnCount));
    }
}
