// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

namespace OmniTray.Core.Tests;

[TestClass]
public sealed class StickyNoteTests
{
    [TestMethod]
    public void EmptyNoteIsAnOpenableItem()
    {
        var note = StickyNote.Create();
        var item = DropItem.CreateNote(note);
        Assert.AreEqual("New note", item.DisplayName);
        Assert.AreEqual(note.Id, item.Id);
        Assert.AreEqual(note.CreatedAt, note.UpdatedAt);
        Assert.IsTrue(ContentMetadataPolicy.HasAction(item, ContentActions.Open));
        Assert.IsFalse(ContentMetadataPolicy.HasAction(item, ContentActions.Delete));
    }

    [TestMethod]
    public void EditsPreserveIdentityAndCreationTimeIncludingClearAndFormattingOnlyEdits()
    {
        var original = StickyNote.Create("Hello", @"{\rtf1 Hello}");
        Assert.AreSame(original, original.Update(original.Text, original.Rtf, original.Color));
        var formatted = original.Update("Hello", @"{\rtf1\b Hello}", NoteColor.Mint);
        var cleared = formatted.Update("", null, NoteColor.Mint);
        Assert.AreEqual(original.Id, cleared.Id);
        Assert.AreEqual(original.CreatedAt, cleared.CreatedAt);
        Assert.IsTrue(formatted.UpdatedAt > original.UpdatedAt);
        Assert.IsTrue(cleared.UpdatedAt > formatted.UpdatedAt);
        Assert.AreEqual("", cleared.Text);
        Assert.IsNull(cleared.Rtf);
        Assert.AreEqual(NoteColor.Mint, cleared.Color);
    }

    [TestMethod]
    public void DuplicateIsIndependent()
    {
        var original = StickyNote.Create("Hello", @"{\rtf1\b Hello}", NoteColor.Pink);
        var copy = original.Duplicate();
        Assert.AreNotEqual(original.Id, copy.Id);
        Assert.AreEqual(original.Text, copy.Text);
        Assert.AreEqual(original.Rtf, copy.Rtf);
        Assert.AreEqual(original.Color, copy.Color);
        Assert.AreEqual(copy.CreatedAt, copy.UpdatedAt);
    }

    [TestMethod]
    public void NoteContentRemainsAuthoritativeForItemRepresentations()
    {
        var note = StickyNote.Create("Current", null, NoteColor.Yellow);
        var item = DropItem.CreateNote(note).WithRepresentations("Stale", rtf: @"{\rtf1 Stale}");
        Assert.AreEqual(note.Text, item.Text);
        Assert.IsNull(item.Rtf);
        Assert.AreEqual(note.CreatedAt, item.CreatedAt);
    }

    [TestMethod]
    public void RelocateTraversesAllPlacementsWithoutChangingNote()
    {
        var note = StickyNote.Create("A note", @"{\rtf1 A note}", NoteColor.Blue);
        var parent = DropItem.CreateText("Parent");
        var first = DropStack.Create([DropItem.CreateNote(note)]);
        var second = DropStack.Create([parent]);
        IReadOnlyList<DropStack> stacks = [first, second];
        var targets = new[]
        {
            new NoteTarget(second.Id, NotePlacement.StackItem),
            new NoteTarget(second.Id, NotePlacement.Item, parent.Id),
            new NoteTarget(first.Id, NotePlacement.StackItem)
        };
        foreach (var target in targets)
        {
            stacks = NoteOperations.Relocate(stacks, note.Id, target);
            var location = NoteOperations.Find(stacks, note.Id)!;
            Assert.AreEqual(target, location.Target);
            Assert.AreSame(note, location.Note);
            Assert.HasCount(1, NoteOperations.Enumerate(stacks).ToArray());
        }
        Assert.AreSame(stacks, NoteOperations.Relocate(stacks, note.Id, targets[^1]));
    }

    [TestMethod]
    public void InvalidMoveDoesNotLoseOriginalNote()
    {
        var note = StickyNote.Create("Keep me");
        var stack = DropStack.Create([DropItem.CreateNote(note)]);
        Assert.Throws<ArgumentException>(() => NoteOperations.Relocate([stack], note.Id,
            new NoteTarget(Guid.NewGuid(), NotePlacement.StackItem)));
        Assert.Throws<ArgumentException>(() => NoteOperations.Relocate([stack], note.Id,
            new NoteTarget(stack.Id, NotePlacement.Item, note.Id)));
        Assert.AreSame(note, stack.Items[0].Note);
    }

    [TestMethod]
    [DataRow(NotePlacement.StackItem)]
    [DataRow(NotePlacement.Item)]
    public void UpdateAndDeleteWorkInEveryPlacement(NotePlacement placement)
    {
        var parent = DropItem.CreateText("Parent");
        var stack = DropStack.Create([parent]);
        var note = StickyNote.Create("Before");
        var target = new NoteTarget(stack.Id, placement, placement == NotePlacement.Item ? parent.Id : null);
        var added = NoteOperations.Add([stack], note, target);
        var edited = note.Update("After", @"{\rtf1\i After}", NoteColor.Lavender);
        var updated = NoteOperations.Update(added, edited);
        Assert.AreEqual(target, NoteOperations.Find(updated, note.Id)!.Target);
        Assert.AreEqual(edited, NoteOperations.Find(updated, note.Id)!.Note);
        if (placement == NotePlacement.StackItem)
        {
            var item = updated[0].Items.Single(item => item.Id == note.Id);
            Assert.AreEqual(edited.Text, item.Text);
            Assert.AreEqual(edited.Rtf, item.Rtf);
            Assert.AreEqual(edited.DisplayName, item.DisplayName);
        }
        var deleted = NoteOperations.Delete(updated, note.Id);
        Assert.IsNull(NoteOperations.Find(deleted, note.Id));
        Assert.AreEqual(parent.Id, deleted[0].Items[0].Id);
        Assert.AreEqual(parent.Text, deleted[0].Items[0].Text);
        Assert.HasCount(0, deleted[0].Items[0].AttachedNotes);
        Assert.Throws<ArgumentException>(() => NoteOperations.Update(deleted, edited));
    }

    [TestMethod]
    public void DuplicatePlacementIsRejected()
    {
        var note = StickyNote.Create();
        var stack = DropStack.Create([DropItem.CreateNote(note)]);
        Assert.Throws<ArgumentException>(() => NoteOperations.Add([stack], note,
            new NoteTarget(stack.Id, NotePlacement.StackItem)));
        Assert.Throws<ArgumentException>(() => NoteOperations.Validate([stack, DropStack.Create([DropItem.CreateText("Parent").WithAttachedNotes([note])])]));
    }
    [TestMethod]
    public void LegacyStackTargetNormalizesAndDoesNotReorderAnExistingNote()
    {
        var note = StickyNote.Create("Keep position");
        var stack = DropStack.Create([DropItem.CreateText("Before")]);
        var added = NoteOperations.Add([stack], note, new NoteTarget(stack.Id, NotePlacement.LegacyStack));
        Assert.AreEqual(new NoteTarget(stack.Id, NotePlacement.StackItem), NoteOperations.Find(added, note.Id)!.Target);
        var withTail = added[0].Append([DropItem.CreateText("After")]);
        IReadOnlyList<DropStack> current = [withTail];
        Assert.AreSame(current, NoteOperations.Relocate(current, note.Id, new NoteTarget(stack.Id, NotePlacement.LegacyStack)));
        Assert.AreSame(note, current[0].Items[1].Note);
    }

    [TestMethod]
    public void StackOverlayNotesFollowItemOrderAndExcludeItemAttachments()
    {
        var first = StickyNote.Create("First");
        var second = StickyNote.Create("Second");
        var parent = DropItem.CreateText("Parent").WithAttachedNotes([StickyNote.Create("Annotation")]);
        var stack = DropStack.Create([DropItem.CreateNote(first), parent, DropItem.CreateNote(second)]);
        stack = StackOperations.MoveItemsWithin(stack, [second.Id], 0);
        CollectionAssert.AreEqual(new[] { second, first }, NoteOperations.GetStackNotes(stack).ToArray());
    }

}
