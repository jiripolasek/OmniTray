using System.Drawing;
using OmniTray.Services;

namespace OmniTray.Core.Tests;

[TestClass]
public sealed class TrayWindowPlacementTests
{
    [TestMethod]
    public void ExpansionAlignsThumbnailAnchorsAndClampsToWorkArea()
    {
        var workArea = new Rectangle(0, 0, 1920, 1080);
        var compact = new Rectangle(800, 400, 196, 242);
        var compactThumbnailCenter = new Point(98, 112);
        var middleGridThumbnailCenter = new Point(200, 267);

        var centered = TrayWindowPlacement.GetExpansion(
            compact,
            new Size(400, 520),
            compactThumbnailCenter,
            middleGridThumbnailCenter,
            workArea,
            20);
        Assert.AreEqual(new Rectangle(698, 245, 400, 520), centered.Bounds);
        Assert.AreEqual(0.5, centered.HorizontalOrigin);
        Assert.AreEqual(155d / 278d, centered.VerticalOrigin);
        Assert.AreEqual(
            compact.Left + compactThumbnailCenter.X,
            centered.Bounds.Left + middleGridThumbnailCenter.X);
        Assert.AreEqual(
            compact.Top + compactThumbnailCenter.Y,
            centered.Bounds.Top + middleGridThumbnailCenter.Y);

        var topLeft = TrayWindowPlacement.GetExpansion(
            new Rectangle(20, 30, 196, 242),
            new Size(400, 520),
            compactThumbnailCenter,
            middleGridThumbnailCenter,
            workArea,
            20);
        Assert.AreEqual(new Rectangle(20, 20, 400, 520), topLeft.Bounds);

        var bottomRight = TrayWindowPlacement.GetExpansion(
            new Rectangle(1700, 800, 196, 242),
            new Size(400, 520),
            compactThumbnailCenter,
            middleGridThumbnailCenter,
            workArea,
            20);
        Assert.AreEqual(new Rectangle(1500, 540, 400, 520), bottomRight.Bounds);

        Assert.AreEqual(
            new Rectangle(1700, 800, 196, 242),
            TrayWindowPlacement.GetCompactBounds(
                bottomRight.Bounds,
                new Size(196, 242),
                bottomRight.HorizontalOrigin,
                bottomRight.VerticalOrigin));
    }

    [TestMethod]
    public void InteractiveBoundsExcludeTheTransparentShadowMargin()
    {
        var hostSize = new Size(464, 584);
        var compactSize = new Size(196, 242);
        var expandedBounds = TrayWindowPlacement.GetInteractiveBounds(
            hostSize,
            compactSize,
            32,
            true,
            0.5,
            0.5);

        Assert.AreEqual(
            new Rectangle(32, 32, 400, 520),
            expandedBounds);
        Assert.AreEqual(
            new Rectangle(32, 32, 400, 14),
            TrayWindowPlacement.GetExpandedDragBounds(expandedBounds, 14));
        Assert.AreEqual(
            new Rectangle(134, 171, 196, 242),
            TrayWindowPlacement.GetInteractiveBounds(hostSize, compactSize, 32, false, 0.5, 0.5));
    }
}
