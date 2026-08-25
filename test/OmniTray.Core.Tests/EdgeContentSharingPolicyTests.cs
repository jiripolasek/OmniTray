// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

namespace OmniTray.Core.Tests;

[TestClass]
public sealed class EdgeContentSharingPolicyTests
{
    [TestMethod]
    [DataRow(EdgeShelfSide.Left)]
    [DataRow(EdgeShelfSide.Right)]
    [DataRow(EdgeShelfSide.Top)]
    [DataRow(EdgeShelfSide.Bottom)]
    public void ResolveContentSource_LeavesIndependentEdgesUnchanged(EdgeShelfSide side)
    {
        Assert.AreEqual(
            side,
            EdgeContentSharingPolicy.ResolveContentSource(
                side,
                false,
                false,
                false));
    }

    [TestMethod]
    public void ResolveContentSource_UsesLeftAndTopAsPairSources()
    {
        Assert.AreEqual(
            EdgeShelfSide.Left,
            EdgeContentSharingPolicy.ResolveContentSource(
                EdgeShelfSide.Right,
                true,
                true,
                false));
        Assert.AreEqual(
            EdgeShelfSide.Top,
            EdgeContentSharingPolicy.ResolveContentSource(
                EdgeShelfSide.Bottom,
                true,
                true,
                false));
    }

    [TestMethod]
    public void ResolveContentSource_PairSharingDoesNotCrossAxes()
    {
        Assert.AreEqual(
            EdgeShelfSide.Bottom,
            EdgeContentSharingPolicy.ResolveContentSource(
                EdgeShelfSide.Bottom,
                true,
                false,
                false));
        Assert.AreEqual(
            EdgeShelfSide.Right,
            EdgeContentSharingPolicy.ResolveContentSource(
                EdgeShelfSide.Right,
                false,
                true,
                false));
    }

    [TestMethod]
    [DataRow(EdgeShelfSide.Left)]
    [DataRow(EdgeShelfSide.Right)]
    [DataRow(EdgeShelfSide.Top)]
    [DataRow(EdgeShelfSide.Bottom)]
    public void ResolveContentSource_AllEdgesUsesLeftAsTheSharedSource(EdgeShelfSide side)
    {
        Assert.AreEqual(
            EdgeShelfSide.Left,
            EdgeContentSharingPolicy.ResolveContentSource(
                side,
                false,
                false,
                true));
    }

    [TestMethod]
    public void ResolveContentSource_RejectsUnknownSide()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            EdgeContentSharingPolicy.ResolveContentSource(
                (EdgeShelfSide)42,
                false,
                false,
                false));
    }
}
