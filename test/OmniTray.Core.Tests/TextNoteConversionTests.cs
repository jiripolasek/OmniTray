// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

namespace OmniTray.Core.Tests;

[TestClass]
public sealed class TextNoteConversionTests
{
    [TestMethod]
    public void ConversionRetainsPositionIdentityTextFormattingAndCreationTime()
    {
        var before = DropItem.CreateText("Before");
        var source = DropItem.CreateText("Hello\r\n世界", @"C:\captures\text.txt", true,
            "<b>Hello</b>", @"{\rtf1\b Hello\par\u19990?\u30028?}");
        var after = DropItem.CreateText("After");
        var stackNote = StickyNote.Create("Stack annotation");
        var stack = DropStack.Create([before, source, after]).Rename("Work")
            .ChangeTint("Mint").ChangeInspectorViewMode(StackInspectorViewMode.Grid)
            .Append([DropItem.CreateNote(stackNote)]);
        var start = DateTimeOffset.UtcNow;

        var (updated, note) = NoteOperations.ConvertTextItem(stack, source.Id, false);

        Assert.AreEqual(source.Id, note.Id);
        Assert.AreEqual(source.Text, note.Text);
        Assert.AreEqual(source.Rtf, note.Rtf);
        Assert.AreEqual(source.CreatedAt, note.CreatedAt);
        Assert.IsTrue(note.UpdatedAt >= start);
        Assert.AreEqual(NoteColor.Yellow, note.Color);
        Assert.AreSame(before, updated.Items[0]);
        Assert.AreSame(note, updated.Items[1].Note);
        Assert.AreSame(after, updated.Items[2]);
        Assert.HasCount(4, updated.Items);
        Assert.AreEqual(stack.Id, updated.Id);
        Assert.AreEqual(stack.Name, updated.Name);
        Assert.AreEqual(stack.Tint, updated.Tint);
        Assert.AreEqual(stack.InspectorViewMode, updated.InspectorViewMode);
        Assert.AreSame(stackNote, updated.Items[3].Note);
        Assert.AreSame(source, stack.Items[1]);
        Assert.IsNull(updated.Items[1].SourcePath);
        Assert.IsFalse(updated.Items[1].IsOwned);
        Assert.IsNull(updated.Items[1].Html);
    }

    [TestMethod]
    public void DuplicationInsertsIndependentNoteBesideUnchangedCapture()
    {
        var attachment = StickyNote.Create("Annotation", color: NoteColor.Pink);
        var source = DropItem.CreateText("Keep me", @"C:\captures\text.txt", true,
                "<b>Keep me</b>", @"{\rtf1\b Keep me}", "https://example.com/")
            .WithAttachedNotes([attachment]);
        var after = DropItem.CreateText("After");
        var stack = DropStack.Create([source, after]);
        var start = DateTimeOffset.UtcNow;

        var (updated, note) = NoteOperations.ConvertTextItem(stack, source.Id, true);

        Assert.AreNotEqual(source.Id, note.Id);
        Assert.AreEqual(source.Text, note.Text);
        Assert.AreEqual(source.Rtf, note.Rtf);
        Assert.IsTrue(note.CreatedAt >= start);
        Assert.AreEqual(note.CreatedAt, note.UpdatedAt);
        Assert.AreSame(source, updated.Items[0]);
        Assert.AreSame(note, updated.Items[1].Note);
        Assert.AreSame(after, updated.Items[2]);
        Assert.HasCount(3, updated.Items);
        Assert.AreSame(attachment, updated.Items[0].AttachedNotes.Single());
        Assert.HasCount(0, updated.Items[1].AttachedNotes);
        var edited = NoteOperations.Update([updated], note.Update("Edited", null, NoteColor.Mint));
        Assert.AreSame(source, edited[0].Items[0]);
        Assert.AreEqual("Edited", edited[0].Items[1].Text);
        NoteOperations.Validate(edited);
    }

    [TestMethod]
    public void ConversionKeepsAttachedNotesBesideReplacement()
    {
        var first = StickyNote.Create("First annotation");
        var second = StickyNote.Create("Second annotation");
        var source = DropItem.CreateText("Convert me").WithAttachedNotes([first, second]);
        var after = DropItem.CreateText("After");
        var stack = DropStack.Create([source, after]);

        var (updated, note) = NoteOperations.ConvertTextItem(stack, source.Id, false);

        CollectionAssert.AreEqual(new[] { source.Id, first.Id, second.Id, after.Id },
            updated.Items.Select(item => item.Id).ToArray());
        Assert.AreSame(note, updated.Items[0].Note);
        Assert.AreSame(first, updated.Items[1].Note);
        Assert.AreSame(second, updated.Items[2].Note);
        Assert.HasCount(0, updated.Items[0].AttachedNotes);
        Assert.HasCount(2, source.AttachedNotes);
        NoteOperations.Validate([updated]);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void HtmlOnlyCaptureUsesReadableText(bool duplicate)
    {
        var source = DropItem.CreateRichText(null, "<p>Hello &amp; <b>world</b></p>", null);
        var (_, note) = NoteOperations.ConvertTextItem(DropStack.Create([source]), source.Id, duplicate);
        Assert.AreEqual("Hello & world", note.Text);
        Assert.IsNull(note.Rtf);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void RtfOnlyCaptureUsesDecodedPlainTextAndOriginalRtf(bool duplicate)
    {
        var source = DropItem.CreateRichText(null, null, @"{\rtf1\b Hello\par world}");
        var (_, note) = NoteOperations.ConvertTextItem(DropStack.Create([source]), source.Id, duplicate,
            "Hello\r\nworld");
        Assert.AreEqual("Hello\r\nworld", note.Text);
        Assert.AreEqual(source.Rtf, note.Rtf);
    }

    [TestMethod]
    public void MissingRtfPlainTextFailsWithoutReplacingOriginal()
    {
        var source = DropItem.CreateRichText(null, null, @"{\rtf1 Hello}");
        var stack = DropStack.Create([source]);
        Assert.Throws<ArgumentException>(() => NoteOperations.ConvertTextItem(stack, source.Id, false));
        Assert.AreSame(source, stack.Items.Single());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void NonTextOrMissingItemIsRejected(bool duplicate)
    {
        var file = DropItem.CreateStorageItem("original.txt", @"C:\original.txt", false);
        var note = DropItem.CreateNote(StickyNote.Create("Existing note"));
        var stack = DropStack.Create([file, note]);
        foreach (var id in new[] { file.Id, note.Id, Guid.NewGuid() })
        {
            Assert.Throws<ArgumentException>(() => NoteOperations.ConvertTextItem(stack, id, duplicate));
        }

        Assert.AreSame(file, stack.Items[0]);
        Assert.AreSame(note, stack.Items[1]);
    }
}
