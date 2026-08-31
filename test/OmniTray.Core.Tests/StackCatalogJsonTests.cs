// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

using System.Text.Json;
using OmniTray.Services;

namespace OmniTray.Core.Tests;

[TestClass]
public sealed class StackCatalogJsonTests
{
    [TestMethod]
    public void ProductionSerializerRoundTripsRecoveryAndFullCaptureHistory()
    {
        var annotation = StickyNote.Create("Annotation Ω", @"{\rtf1 Annotation}", NoteColor.Blue);
        var source = DropItem.CreateText("Original Ω", @"C:\fixtures\capture.txt", true,
                "<b>Original Ω</b>", @"{\rtf1\b Original}", "https://example.com/source", "Fixture browser")
            .WithCustomFormats([DropItemDataFormat.CreateBinary("fixture", new byte[] { 1, 2, 3, 255 })])
            .WithAttachedNotes([annotation]);
        var original = DropStack.Create([source], itemSortMode: StackItemSortMode.Newest);
        var (stack, note) = NoteOperations.ConvertTextItem(original, source.Id, false);
        var deleted = new DeletedNote(StickyNote.Create("Deleted", @"{\rtf1 Deleted}"),
            new NoteTarget(stack.Id, NotePlacement.StackItem), stack.Name, null, DateTimeOffset.UtcNow);
        var history = new NoteCaptureHistory(note.Id, original.Id, original.Name, source, 0, true);
        var tray = new TrayWindowState(stack.Id, 10, 20, 160, 180, true, 300, 400);
        var state = new StackCatalogState([stack], [tray], [new EdgeShelfState(EdgeShelfSide.Left, [stack.Id])],
            [deleted], [history]);
        var json = JsonSerializer.Serialize(StackCatalogJson.CreateDocument(state),
            StackCatalogJsonContext.Default.StackCatalogDocument);
        var restored = StackCatalogJson.Restore(JsonSerializer.Deserialize(json,
            StackCatalogJsonContext.Default.StackCatalogDocument)!);
        var capture = restored.NoteHistory.Single().SourceItem;
        Assert.AreEqual(source.Id, capture.Id);
        Assert.AreEqual(source.Text, capture.Text);
        Assert.AreEqual(source.Rtf, capture.Rtf);
        Assert.AreEqual(source.Html, capture.Html);
        Assert.AreEqual(source.SourcePath, capture.SourcePath);
        Assert.AreEqual(source.SourceUrl, capture.SourceUrl);
        Assert.AreEqual(source.SourceApplicationName, capture.SourceApplicationName);
        Assert.AreEqual(source.CreatedAt, capture.CreatedAt);
        Assert.IsTrue(capture.IsOwned);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 255 }, capture.CustomFormats.Single().GetBinaryData());
        Assert.AreEqual(annotation, capture.AttachedNotes.Single());
        Assert.AreEqual(note, restored.Stacks.Single().Items.First().Note);
        Assert.AreEqual(StackItemSortMode.Newest, restored.Stacks.Single().ItemSortMode);
        Assert.AreEqual(deleted, restored.DeletedNotes.Single());
        Assert.AreEqual(tray, restored.OpenTrayWindows.Single());
        Assert.AreEqual(stack.Id,
            restored.EdgeShelves.Single(shelf => shelf.Side == EdgeShelfSide.Left).StackIds.Single());
    }

    [TestMethod]
    public void ProductionSerializerLoadsLegacyCatalogWithoutNoteHistory()
    {
        var document
            = JsonSerializer.Deserialize("{\"stacks\":[]}", StackCatalogJsonContext.Default.StackCatalogDocument)!;
        var restored = StackCatalogJson.Restore(document);
        Assert.HasCount(0, restored.NoteHistory);
        Assert.HasCount(0, restored.DeletedNotes);
        Assert.HasCount(0, restored.Stacks);
    }

    [TestMethod]
    public void ProductionSerializerPersistsVirtualSourceWithoutLiveItems()
    {
        var source = VirtualStackSource.Create(
            "builtin.folder",
            @"C:\Fixtures",
            VirtualStackCapabilities.Read | VirtualStackCapabilities.Write);
        var stack = DropStack.CreateVirtual("Fixtures", source).RefreshVirtualItems(
            [DropItem.CreateStorageItem("example.txt", @"C:\Fixtures\example.txt", false)]);

        var document = StackCatalogJson.CreateDocument(new StackCatalogState([stack], [], [], [], []));
        Assert.HasCount(0, document.Stacks.Single().Items);

        var restored = StackCatalogJson.Restore(document).Stacks.Single();
        Assert.AreEqual(source, restored.VirtualSource);
        Assert.HasCount(0, restored.Items);
    }

    [TestMethod]
    public void LegacyStackNotesMigrateOnceAndPreserveItemsHistoryAndTimestamps()
    {
        var source = DropItem.CreateText("Existing capture");
        var stack = DropStack.Create([source], "Project");
        var note = StickyNote.Create("Legacy Ω", @"{\rtf1 Legacy}", NoteColor.Lavender)
            .Update("Edited Ω", @"{\rtf1\b Edited}", NoteColor.Pink);
        var deleted = new DeletedNote(StickyNote.Create("Deleted"), new NoteTarget(stack.Id, NotePlacement.LegacyStack),
            stack.Name, null, DateTimeOffset.UtcNow);
        var document = StackCatalogJson.CreateDocument(new StackCatalogState([stack], [], [], [], []));
        document.Stacks[0].AttachedNotes = [note];
        document.DeletedNotes = [deleted];
        document.NoteHistory =
        [
            new NoteHistoryDocument
            {
                NoteId = note.Id,
                SourceStackId = stack.Id,
                SourceStackName = stack.Name,
                SourceItem = document.Stacks[0].Items[0],
                SourceIndex = 0,
                IsConversion = false
            }
        ];
        var legacyJson = JsonSerializer.Serialize(document, StackCatalogJsonContext.Default.StackCatalogDocument);
        var restored
            = StackCatalogJson.Restore(JsonSerializer.Deserialize(legacyJson,
                StackCatalogJsonContext.Default.StackCatalogDocument)!);
        Assert.AreEqual(source.Id, restored.Stacks[0].Items[0].Id);
        Assert.AreEqual(note, restored.Stacks[0].Items[1].Note);
        Assert.AreEqual(note.Id, restored.NoteHistory.Single().NoteId);
        Assert.AreEqual(source.Id, restored.NoteHistory.Single().SourceItem.Id);
        Assert.AreEqual(deleted with { Target = new NoteTarget(stack.Id, NotePlacement.StackItem) },
            restored.DeletedNotes.Single());
        // The Command Palette's independent reader uses the same restore boundary.
        Assert.AreEqual(note, StackCatalogReader.ReadStacks(legacyJson).Single().Items[1].Note);
        var saved = JsonSerializer.Serialize(StackCatalogJson.CreateDocument(restored),
            StackCatalogJsonContext.Default.StackCatalogDocument);
        using var savedDocument = JsonDocument.Parse(saved);
        Assert.IsFalse(savedDocument.RootElement.GetProperty("stacks")[0].TryGetProperty("attachedNotes", out _));
        var reloaded
            = StackCatalogJson.Restore(JsonSerializer.Deserialize(saved,
                StackCatalogJsonContext.Default.StackCatalogDocument)!);
        CollectionAssert.AreEqual(restored.Stacks[0].Items.Select(item => item.Id).ToArray(),
            reloaded.Stacks[0].Items.Select(item => item.Id).ToArray());
        Assert.AreEqual(note, reloaded.Stacks[0].Items[1].Note);
    }
}
