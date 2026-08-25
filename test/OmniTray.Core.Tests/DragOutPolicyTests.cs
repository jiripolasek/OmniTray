// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

namespace OmniTray.Core.Tests;

[TestClass]
public sealed class DragOutPolicyTests
{
    [TestMethod]
    public void ShouldRequestMove_DefaultsToCopy()
    {
        var item = CreateFile();

        Assert.IsFalse(DragOutPolicy.ShouldRequestMove(
            false,
            true,
            false,
            [item]));
        Assert.IsFalse(DragOutPolicy.ShouldRequestMove(
            true,
            false,
            false,
            [item]));
    }

    [TestMethod]
    public void ShouldRequestMove_ShiftEnablesMoveForOriginalPathBackedItems()
    {
        Assert.IsTrue(DragOutPolicy.ShouldRequestMove(
            true,
            true,
            false,
            [CreateFile(), CreateFolder(), CreateImage()]));
    }

    [TestMethod]
    public void ShouldRequestMove_ControlKeepsCopySemantics()
    {
        Assert.IsFalse(DragOutPolicy.ShouldRequestMove(
            true,
            true,
            true,
            [CreateFile()]));
    }

    [TestMethod]
    public void ShouldRequestMove_RejectsOwnedOrMaterializedItems()
    {
        var ownedFile = DropItem.CreateStorageItem(
            "capture.txt",
            @"C:\OmniTray\capture.txt",
            false,
            true);
        var text = DropItem.CreateText(
            "captured text",
            @"C:\OmniTray\text.txt",
            true);

        Assert.IsFalse(DragOutPolicy.ShouldRequestMove(
            true,
            true,
            false,
            [CreateFile(), ownedFile]));
        Assert.IsFalse(DragOutPolicy.ShouldRequestMove(
            true,
            true,
            false,
            [text]));
    }

    [TestMethod]
    public void ShouldRemoveSource_RequiresRequestedAndCompletedMove()
    {
        Assert.IsTrue(DragOutPolicy.ShouldRemoveSource(
            true,
            true));
        Assert.IsFalse(DragOutPolicy.ShouldRemoveSource(
            false,
            true));
        Assert.IsFalse(DragOutPolicy.ShouldRemoveSource(
            true,
            false));
    }

    private static DropItem CreateFile() =>
        DropItem.CreateStorageItem("notes.txt", @"C:\Source\notes.txt", false);

    private static DropItem CreateFolder() =>
        DropItem.CreateStorageItem("Documents", @"C:\Source\Documents", true);

    private static DropItem CreateImage() =>
        DropItem.CreateImage("photo.png", @"C:\Source\photo.png");
}
