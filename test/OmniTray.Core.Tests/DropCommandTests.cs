// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

namespace OmniTray.Core.Tests;

[TestClass]
public sealed class DropCommandTests
{
    [TestMethod]
    public void Restore_PreservesUnknownStringTemplateAndParameters()
    {
        var id = Guid.NewGuid();
        var command = DropCommandInstance.Restore(
            id,
            "example.provider.future-command",
            "Future command",
            new Dictionary<string, string> { ["futureValue"] = "preserve me" },
            false,
            "Iris");

        Assert.AreEqual(id, command.Id);
        Assert.AreEqual("example.provider.future-command", command.TemplateId);
        Assert.AreEqual("preserve me", command.Parameters["futureValue"]);
        Assert.IsFalse(command.IsEnabled);
        Assert.AreEqual("Iris", command.Tint);
    }

    [TestMethod]
    public void Reconfigure_PreservesTrayTint()
    {
        var command = DropCommandInstance.Create(
                "example.command",
                "Example",
                tint: "Teal")
            .Reconfigure(
                "Updated",
                new Dictionary<string, string> { ["value"] = "updated" },
                false);

        Assert.AreEqual("Teal", command.Tint);
        Assert.AreEqual("Updated", command.DisplayName);
        Assert.IsFalse(command.IsEnabled);
    }

    [TestMethod]
    public void RestoreLayout_AllowsFoldersAndLeafReferences()
    {
        var commandId = Guid.NewGuid();
        var folder = DropCommandFolderNode.Create(null, 0, "File management");
        var leaf = DropCommandLeafNode.Create(folder.Id, 0, commandId);

        var layout = DropCommandSurfaceLayout.Restore(
            DropCommandSurfaceIds.ForEdge(EdgeShelfSide.Left),
            [folder, leaf]);

        Assert.AreEqual("edge:left", layout.SurfaceId);
        Assert.HasCount(2, layout.Nodes);
    }

    [TestMethod]
    public void RestoreLayout_RejectsMissingFolderParent()
    {
        var leaf = DropCommandLeafNode.Create(Guid.NewGuid(), 0, Guid.NewGuid());

        Assert.ThrowsExactly<ArgumentException>(() =>
            DropCommandSurfaceLayout.Restore(DropCommandSurfaceIds.Popup, [leaf]));
    }

    [TestMethod]
    public void RestoreLayout_RejectsMissingFolderAncestor()
    {
        var missingId = Guid.NewGuid();
        var folder = DropCommandFolderNode.Restore(Guid.NewGuid(), missingId, 0, "Nested");
        var leaf = DropCommandLeafNode.Create(folder.Id, 0, Guid.NewGuid());

        Assert.ThrowsExactly<ArgumentException>(() =>
            DropCommandSurfaceLayout.Restore(DropCommandSurfaceIds.Popup, [leaf, folder]));
    }

    [TestMethod]
    public void RestoreLayout_RejectsNegativeOrder()
    {
        var leaf = DropCommandLeafNode.Restore(Guid.NewGuid(), null, -1, Guid.NewGuid());

        Assert.ThrowsExactly<ArgumentException>(() =>
            DropCommandSurfaceLayout.Restore(DropCommandSurfaceIds.Popup, [leaf]));
    }

    [TestMethod]
    public void RestoreLayout_RejectsDuplicateCommandPlacement()
    {
        var commandId = Guid.NewGuid();
        var first = DropCommandLeafNode.Create(null, 0, commandId);
        var second = DropCommandLeafNode.Create(null, 1, commandId);

        Assert.ThrowsExactly<ArgumentException>(() =>
            DropCommandSurfaceLayout.Restore(DropCommandSurfaceIds.Popup, [first, second]));
    }

    [TestMethod]
    public void RestoreLayout_RejectsFolderCycle()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var first = DropCommandFolderNode.Restore(firstId, secondId, 0, "First");
        var second = DropCommandFolderNode.Restore(secondId, firstId, 0, "Second");

        Assert.ThrowsExactly<ArgumentException>(() =>
            DropCommandSurfaceLayout.Restore(DropCommandSurfaceIds.Popup, [first, second]));
    }
}
