// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

using System.Drawing;

namespace OmniTray.Services;

internal readonly record struct TrayWindowExpansion(
    Rectangle Bounds,
    double HorizontalOrigin,
    double VerticalOrigin);

internal static class TrayWindowPlacement
{
    public static TrayWindowExpansion GetExpansion(
        Rectangle compactBounds,
        Size requestedSize,
        Point compactAnchor,
        Point expandedAnchor,
        Rectangle workArea,
        int workAreaInset)
    {
        var inset = Math.Max(0, workAreaInset);
        workArea.Inflate(-inset, -inset);
        var width = Math.Min(requestedSize.Width, workArea.Width);
        var height = Math.Min(requestedSize.Height, workArea.Height);
        var x = Math.Clamp(
            compactBounds.Left + compactAnchor.X - expandedAnchor.X,
            workArea.Left,
            workArea.Right - width);
        var y = Math.Clamp(
            compactBounds.Top + compactAnchor.Y - expandedAnchor.Y,
            workArea.Top,
            workArea.Bottom - height);

        return new TrayWindowExpansion(
            new Rectangle(x, y, width, height),
            GetExpansionOrigin(compactBounds.Left - x, width - compactBounds.Width),
            GetExpansionOrigin(compactBounds.Top - y, height - compactBounds.Height));
    }

    public static Rectangle GetCompactBounds(
        Rectangle expandedBounds,
        Size compactSize,
        double horizontalOrigin,
        double verticalOrigin) =>
        new(
            expandedBounds.Left + GetExpansionOffset(
                expandedBounds.Width - compactSize.Width,
                horizontalOrigin),
            expandedBounds.Top + GetExpansionOffset(
                expandedBounds.Height - compactSize.Height,
                verticalOrigin),
            compactSize.Width,
            compactSize.Height);

    public static Rectangle GetInteractiveBounds(
        Size hostSize,
        Size compactSize,
        int shadowMargin,
        bool expanded,
        double horizontalOrigin,
        double verticalOrigin)
    {
        var contentBounds = new Rectangle(
            shadowMargin,
            shadowMargin,
            Math.Max(0, hostSize.Width - (shadowMargin * 2)),
            Math.Max(0, hostSize.Height - (shadowMargin * 2)));
        return expanded
            ? contentBounds
            : GetCompactBounds(contentBounds, compactSize, horizontalOrigin, verticalOrigin);
    }

    public static Rectangle GetExpandedDragBounds(Rectangle interactiveBounds, int dragHeight) =>
        new(
            interactiveBounds.X,
            interactiveBounds.Y,
            interactiveBounds.Width,
            Math.Clamp(dragHeight, 0, interactiveBounds.Height));

    private static double GetExpansionOrigin(int offset, int availableExpansion) =>
        availableExpansion <= 0
            ? 0
            : Math.Clamp(offset / (double)availableExpansion, 0, 1);

    private static int GetExpansionOffset(int availableExpansion, double origin) =>
        availableExpansion <= 0
            ? 0
            : (int)Math.Round(availableExpansion * Math.Clamp(origin, 0, 1));
}
