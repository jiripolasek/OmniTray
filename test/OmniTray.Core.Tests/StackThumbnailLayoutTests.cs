// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

using OmniTray.Controls;

namespace OmniTray.Core.Tests;

[TestClass]
public sealed class StackThumbnailLayoutTests
{
    [TestMethod]
    [DataRow(160d)]
    [DataRow(319d)]
    [DataRow(320d)]
    [DataRow(600d)]
    [DataRow(640d)]
    [DataRow(1920d)]
    public void GetItemWidth_PreferredWidthStaysStableAcrossWindowSizes(double availableWidth)
    {
        Assert.AreEqual(160d, StackThumbnailLayout.GetItemWidth(availableWidth, 160));
    }

    [TestMethod]
    [DataRow(80d)]
    [DataRow(159.5d)]
    public void GetItemWidth_NarrowViewportFitsOneTile(double availableWidth)
    {
        Assert.AreEqual(availableWidth, StackThumbnailLayout.GetItemWidth(availableWidth, 160));
    }

    [TestMethod]
    [DataRow(240d, 96d)]
    [DataRow(324d, 108d)]
    [DataRow(500d, 166d)]
    public void GetItemWidth_UnsetPreferredWidthPreservesCompactLayout(double availableWidth, double expectedWidth)
    {
        Assert.AreEqual(expectedWidth, StackThumbnailLayout.GetItemWidth(availableWidth, double.NaN));
    }

    [TestMethod]
    [DataRow(0d)]
    [DataRow(-1d)]
    [DataRow(double.NaN)]
    [DataRow(double.PositiveInfinity)]
    [DataRow(double.NegativeInfinity)]
    public void GetItemWidth_InvalidViewportDoesNotRequestLayout(double availableWidth)
    {
        Assert.AreEqual(0d, StackThumbnailLayout.GetItemWidth(availableWidth, 160));
        Assert.AreEqual(0d, StackThumbnailLayout.GetItemWidth(availableWidth, double.NaN));
    }

    [TestMethod]
    [DataRow(0d)]
    [DataRow(-1d)]
    [DataRow(double.PositiveInfinity)]
    [DataRow(double.NegativeInfinity)]
    public void GetItemWidth_InvalidPreferredWidthUsesCompactLayout(double preferredWidth)
    {
        Assert.AreEqual(108d, StackThumbnailLayout.GetItemWidth(324, preferredWidth));
    }

    [TestMethod]
    public void GetItemWidth_ResizingBackAndForthDoesNotAccumulateChanges()
    {
        double[] viewportWidths = [600, 1920, 320, 80, 640, 600];
        double[] expectedWidths = [160, 160, 160, 80, 160, 160];

        for (var index = 0; index < viewportWidths.Length; index++)
        {
            Assert.AreEqual(expectedWidths[index], StackThumbnailLayout.GetItemWidth(viewportWidths[index], 160));
        }
    }
}
