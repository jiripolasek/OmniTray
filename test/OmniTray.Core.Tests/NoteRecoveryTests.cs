// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Text.Json;

namespace OmniTray.Core.Tests;

[TestClass]
public sealed class NoteRecoveryTests
{
    [TestMethod]
    public void HistoryValidationRejectsDuplicateDeletedNotesAndOrphanedSources()
    {
        var source = DropItem.CreateText("Original");
        var stack = DropStack.Create([source]);
        var note = StickyNote.Create("Deleted");
        var deleted = new DeletedNote(note, new NoteTarget(stack.Id, NotePlacement.StackItem),
            stack.Name, null, DateTimeOffset.UtcNow);
        var history = new NoteCaptureHistory(note.Id, stack.Id, stack.Name, source, 0, true);
        NoteRecovery.ValidateHistory([stack], [deleted], [history]);
        Assert.Throws<ArgumentException>(() => NoteRecovery.ValidateHistory([stack], [deleted, deleted], [history]));
        Assert.Throws<ArgumentException>(() => NoteRecovery.ValidateHistory([stack], [], [history]));
        Assert.Throws<ArgumentException>(() => NoteRecovery.ValidateHistory([stack], [deleted], [history, history]));
    }

    [TestMethod]
    public void RepeatedConversionPreservesPreviouslyDeletedEditsAndCaptureHistory()
    {
        var source = DropItem.CreateText("Original");
        var stack = DropStack.Create([source]);
        var (converted, note) = NoteOperations.ConvertTextItem(stack, source.Id, false);
        var capture = new NoteCaptureHistory(note.Id, stack.Id, stack.Name, source, 0, true);
        var edited = NoteOperations.Update([converted], note.Update("First edits", null, NoteColor.Peach));
        var restored = NoteRecovery.UndoConversion(edited, capture);
        var deleted = NoteRecovery.FindRemoved(edited, restored, DateTimeOffset.UtcNow);
        var (convertedAgain, nextNote) = NoteOperations.ConvertTextItem(restored.Single(), source.Id, false);
        var (nextDeleted, nextHistory) = NoteRecovery.RecordCapture(deleted, [capture], capture with { NoteId = nextNote.Id });
        NoteOperations.Validate([convertedAgain]);
        NoteRecovery.ValidateHistory([convertedAgain], nextDeleted, nextHistory);
        var oldEdits = nextDeleted.Single().Note;
        Assert.AreNotEqual(nextNote.Id, oldEdits.Id);
        Assert.AreEqual("First edits", oldEdits.Text);
        Assert.AreEqual(deleted.Single().Note.UpdatedAt, oldEdits.UpdatedAt);
        Assert.HasCount(2, nextHistory);
        Assert.AreSame(source, nextHistory.Single(entry => entry.NoteId == oldEdits.Id).SourceItem);
        var (recovered, _) = NoteRecovery.Restore([convertedAgain], nextDeleted.Single());
        Assert.HasCount(2, recovered.Single().Items);
    }

    [TestMethod]
    public void RemovingStackCapturesEveryNoteButMovingDoesNot()
    {
        var itemNote = StickyNote.Create("Item note");
        var attached = StickyNote.Create("Annotation");
        var stackNote = StickyNote.Create("Stack note");
        var stack = DropStack.Create([DropItem.CreateNote(itemNote),
            DropItem.CreateText("Parent").WithAttachedNotes([attached])]).Append([DropItem.CreateNote(stackNote)]);
        var removed = NoteRecovery.FindRemoved([stack], [], DateTimeOffset.UtcNow);
        Assert.HasCount(3, removed);
        Assert.AreEqual("Parent", removed.Single(entry => entry.Note.Id == attached.Id).ItemName);
        var moved = NoteOperations.Relocate([stack], attached.Id, new NoteTarget(stack.Id, NotePlacement.StackItem));
        Assert.HasCount(0, NoteRecovery.FindRemoved([stack], moved, DateTimeOffset.UtcNow));
    }

    [TestMethod]
    public void RestoreRecreatesMissingStackWithoutRestoringUnrelatedItems()
    {
        var note = StickyNote.Create("Keep", @"{\rtf1\b Keep}", NoteColor.Pink);
        var deleted = new DeletedNote(note, new NoteTarget(Guid.NewGuid(), NotePlacement.Item, Guid.NewGuid()),
            "Deleted project", "Missing parent", DateTimeOffset.UtcNow);
        var serialized = JsonSerializer.Serialize(deleted);
        var (stacks, restored) = NoteRecovery.Restore([], JsonSerializer.Deserialize<DeletedNote>(serialized)!);
        Assert.AreEqual(note, restored);
        Assert.AreEqual(deleted.Target.StackId, stacks.Single().Id);
        Assert.AreEqual("Deleted project", stacks.Single().Name);
        Assert.AreEqual(note, stacks.Single().Items.Single().Note);
    }

    [TestMethod]
    public void RestoreReattachesToExistingParent()
    {
        var parent = DropItem.CreateText("Parent");
        var stack = DropStack.Create([parent]);
        var note = StickyNote.Create("Keep");
        var deleted = new DeletedNote(note, new NoteTarget(stack.Id, NotePlacement.Item, parent.Id),
            stack.Name, parent.DisplayName, DateTimeOffset.UtcNow);
        var (stacks, _) = NoteRecovery.Restore([stack], deleted);
        Assert.AreSame(note, stacks.Single().Items.Single().AttachedNotes.Single());
    }

    [TestMethod]
    public void RestoreWithOccupiedIdentityPreservesContentAndHistoryWithFreshIdentity()
    {
        var source = DropItem.CreateText("Original");
        var stack = DropStack.Create([source]);
        var note = new StickyNote(source.Id, "Edited", @"{\rtf1 Edited}", NoteColor.Blue,
            source.CreatedAt, source.CreatedAt.AddSeconds(1));
        var deleted = new DeletedNote(note, new NoteTarget(stack.Id, NotePlacement.StackItem), stack.Name, null,
            DateTimeOffset.UtcNow);
        var (stacks, restored) = NoteRecovery.Restore([stack], deleted);
        Assert.AreNotEqual(note.Id, restored.Id);
        Assert.AreEqual(note.Text, restored.Text);
        Assert.AreEqual(note.Rtf, restored.Rtf);
        Assert.AreEqual(note.Color, restored.Color);
        Assert.AreEqual(note.CreatedAt, restored.CreatedAt);
        Assert.AreEqual(note.UpdatedAt, restored.UpdatedAt);
        Assert.AreSame(source, stacks.Single().Items.First());
    }

    [TestMethod]
    public void UndoRestoresCaptureAndCurrentAnnotationsAndArchivesEditedNote()
    {
        var annotation = StickyNote.Create("Annotation");
        var source = DropItem.CreateText("Original", @"C:\capture.txt", true, "<b>Original</b>",
            @"{\rtf1\b Original}", "https://example.com", "Browser").WithAttachedNotes([annotation]);
        var original = DropStack.Create([source]);
        var (converted, note) = NoteOperations.ConvertTextItem(original, source.Id, false);
        var history = new NoteCaptureHistory(note.Id, original.Id, original.Name, source, 0, true);
        var edited = NoteOperations.Update([converted], note.Update("Edited", null, NoteColor.Mint));
        var changedAnnotation = annotation.Update("Changed annotation", null, NoteColor.Blue);
        edited = NoteOperations.Update(edited, changedAnnotation);
        var restored = NoteRecovery.UndoConversion(edited, history);
        var item = restored.Single().Items.Single();
        Assert.AreEqual(source.Id, item.Id);
        Assert.AreEqual(source.Text, item.Text);
        Assert.AreEqual(source.Rtf, item.Rtf);
        Assert.AreEqual(source.Html, item.Html);
        Assert.AreEqual(source.SourcePath, item.SourcePath);
        Assert.AreEqual(source.SourceUrl, item.SourceUrl);
        Assert.AreEqual(source.SourceApplicationName, item.SourceApplicationName);
        Assert.IsTrue(item.IsOwned);
        Assert.AreSame(changedAnnotation, item.AttachedNotes.Single());
        var removed = NoteRecovery.FindRemoved(edited, restored, DateTimeOffset.UtcNow);
        Assert.AreEqual("Edited", removed.Single().Note.Text);
        NoteOperations.Validate(restored);
    }

    [TestMethod]
    public void UndoDoesNotResurrectDeletedOrMoveReassociatedAnnotations()
    {
        var removed = StickyNote.Create("Deleted");
        var moved = StickyNote.Create("Moved");
        var source = DropItem.CreateText("Original").WithAttachedNotes([removed, moved]);
        var original = DropStack.Create([source]);
        var (converted, note) = NoteOperations.ConvertTextItem(original, source.Id, false);
        var changed = NoteOperations.Delete([converted], removed.Id);
        var destination = DropStack.CreateEmpty("Elsewhere");
        changed = NoteOperations.Relocate([.. changed, destination], moved.Id,
            new NoteTarget(destination.Id, NotePlacement.StackItem));
        var restored = NoteRecovery.UndoConversion(changed,
            new NoteCaptureHistory(note.Id, original.Id, original.Name, source, 0, true));
        Assert.HasCount(0, restored.Single(stack => stack.Id == original.Id).Items.Single().AttachedNotes);
        Assert.AreSame(moved, restored.Single(stack => stack.Id == destination.Id).Items.Single().Note);
        Assert.IsNull(NoteOperations.Find(restored, removed.Id));
    }

    [TestMethod]
    public void DuplicateCannotBeUndoneAsConversion()
    {
        var source = DropItem.CreateText("Original");
        var stack = DropStack.Create([source]);
        var (updated, note) = NoteOperations.ConvertTextItem(stack, source.Id, true);
        Assert.Throws<ArgumentException>(() => NoteRecovery.UndoConversion([updated],
            new NoteCaptureHistory(note.Id, stack.Id, stack.Name, source, 0, false)));
        Assert.AreSame(source, updated.Items.First());
    }

    [TestMethod]
    public void UndoKeepsPositionRelativeToOtherItemsAfterAnnotationReordering()
    {
        var annotation = StickyNote.Create("Annotation");
        var before = DropItem.CreateText("Before");
        var source = DropItem.CreateText("Original").WithAttachedNotes([annotation]);
        var after = DropItem.CreateText("After");
        var stack = DropStack.Create([before, source, after]);
        var (converted, note) = NoteOperations.ConvertTextItem(stack, source.Id, false);
        converted = StackOperations.MoveItemsWithin(converted, [annotation.Id], 0);
        var restored = NoteRecovery.UndoConversion([converted],
            new NoteCaptureHistory(note.Id, stack.Id, stack.Name, source, 1, true));
        CollectionAssert.AreEqual(new[] { before.Id, source.Id, after.Id }, restored.Single().Items.Select(item => item.Id).ToArray());
    }

    [TestMethod]
    public void SearchFindsNotesInEveryPlacementAndReturnsTheirOwners()
    {
        var direct = StickyNote.Create("Needle direct");
        var stackNote = StickyNote.Create("Needle stack");
        var attachment = StickyNote.Create("Needle annotation");
        var parent = DropItem.CreateText("Parent").WithAttachedNotes([attachment]);
        var stack = DropStack.Create([DropItem.CreateNote(direct), parent]).Append([DropItem.CreateNote(stackNote)]);
        Assert.IsTrue(StackFilter.Matches(stack, "annotation"));
        Assert.IsTrue(StackFilter.Matches(stack, "Needle stack"));
        var matches = StackCatalogSearch.Find([stack], "Needle");
        Assert.HasCount(3, matches);
        Assert.AreEqual(parent.Id, matches.Single(match => match.NoteId == attachment.Id).ItemId);
        Assert.AreEqual(stackNote.Id, matches.Single(match => match.NoteId == stackNote.Id).ItemId);
        Assert.AreEqual(direct.Id, matches.Single(match => match.NoteId == direct.Id).ItemId);
        Assert.IsTrue(matches.All(match => match.StackId == stack.Id && match.Preview.Contains("Needle")));
    }
    [TestMethod]
    public void LegacyDeletedStackNoteRestoresAsAnOrdinaryItem()
    {
        var note = StickyNote.Create("Recovered", @"{\rtf1 Recovered}", NoteColor.Mint);
        var deleted = new DeletedNote(note, new NoteTarget(Guid.NewGuid(), NotePlacement.LegacyStack),
            "Recovered stack", null, DateTimeOffset.UtcNow);
        var (stacks, restored) = NoteRecovery.Restore([], deleted);
        Assert.AreSame(note, restored);
        Assert.AreSame(note, stacks.Single().Items.Single().Note);
        Assert.AreEqual(NotePlacement.StackItem, NoteOperations.Find(stacks, note.Id)!.Target.Placement);
    }

}
