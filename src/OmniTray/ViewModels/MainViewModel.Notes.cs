// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

namespace OmniTray.ViewModels;

public partial class MainViewModel
{
    private readonly List<DeletedNote> _deletedNotes = [];
    private readonly List<NoteCaptureHistory> _noteHistory = [];
    private DropStack[] _lastNoteStacks = [];

    public IReadOnlyList<DeletedNote> DeletedNotes => this._deletedNotes;

    public IReadOnlyList<NoteCaptureHistory> NoteHistory => this._noteHistory;

    internal void RestoreNoteHistory(IEnumerable<DeletedNote> deleted, IEnumerable<NoteCaptureHistory> history)
    {
        this._deletedNotes.Clear();
        this._deletedNotes.AddRange(deleted);
        this._noteHistory.Clear();
        this._noteHistory.AddRange(history);
        this._lastNoteStacks = this.GetNoteStacks();
    }

    private void PublishNoteCatalogChange()
    {
        var current = this.GetNoteStacks();
        foreach (var deleted in NoteRecovery.FindRemoved(this._lastNoteStacks, current, DateTimeOffset.UtcNow))
        {
            this._deletedNotes.RemoveAll(item => item.Note.Id == deleted.Note.Id);
            this._deletedNotes.Add(deleted);
        }

        this._lastNoteStacks = current;
        this.CatalogChanged?.Invoke(this, EventArgs.Empty);
    }

    public NoteLocation? FindNote(Guid noteId) =>
        NoteOperations.Find(this.Stacks.Select(static stack => stack.Model), noteId);

    public StickyNote CreateNote(NoteTarget target, string text = "", string? rtf = null)
    {
        var note = StickyNote.Create(text, rtf);
        this.ApplyNoteChanges(NoteOperations.Add(this.GetNoteStacks(), note, target));
        return note;
    }

    public StickyNote ConvertTextToNote(Guid stackId, Guid itemId, bool duplicate, string? plainText = null)
    {
        var stacks = this.GetNoteStacks();
        var source = stacks.SingleOrDefault(stack => stack.Id == stackId)
                     ?? throw new ArgumentException("The source stack no longer exists.", nameof(stackId));
        var (updatedStack, note) = NoteOperations.ConvertTextItem(source, itemId, duplicate, plainText);
        var updated = stacks.Select(stack => stack.Id == stackId ? updatedStack : stack).ToArray();
        NoteOperations.Validate(updated);
        var original = source.Items.Single(item => item.Id == itemId);
        var (deleted, history) = NoteRecovery.RecordCapture(this._deletedNotes, this._noteHistory,
            new NoteCaptureHistory(note.Id, source.Id, source.Name, original,
                source.Items.ToList().FindIndex(item => item.Id == itemId), !duplicate));
        this._deletedNotes.Clear();
        this._deletedNotes.AddRange(deleted);
        this._noteHistory.Clear();
        this._noteHistory.AddRange(history);
        this.ApplyNoteChanges(updated);
        return note;
    }

    public StickyNote RestoreDeletedNote(Guid noteId)
    {
        var deleted = this._deletedNotes.Single(item => item.Note.Id == noteId);
        var (stacks, note) = NoteRecovery.Restore(this.GetNoteStacks(), deleted);
        if (note.Id != noteId && this._noteHistory.FirstOrDefault(item => item.NoteId == noteId) is { } history)
        {
            this._noteHistory.Remove(history);
            this._noteHistory.Add(history with { NoteId = note.Id });
        }

        this._deletedNotes.Remove(deleted);
        this.ApplyNoteChanges(stacks);
        return note;
    }

    public void PermanentlyDeleteNote(Guid noteId)
    {
        if (this._deletedNotes.RemoveAll(item => item.Note.Id == noteId) > 0)
        {
            this._noteHistory.RemoveAll(item => item.NoteId == noteId);
            this.RequestCatalogChange();
        }
    }

    internal void RestoreRecoveryEntry(DeletedNote deleted, IEnumerable<NoteCaptureHistory> history)
    {
        this._deletedNotes.RemoveAll(entry => entry.Note.Id == deleted.Note.Id);
        this._deletedNotes.Add(deleted);
        this._noteHistory.RemoveAll(entry => entry.NoteId == deleted.Note.Id);
        this._noteHistory.AddRange(history);
        this.RequestCatalogChange();
    }

    public void UndoNoteConversion(Guid noteId)
    {
        var history = this._noteHistory.Single(item => item.NoteId == noteId);
        this.ApplyNoteChanges(NoteRecovery.UndoConversion(this.GetNoteStacks(), history));
    }

    public void UpdateNote(Guid noteId, string text, string? rtf, NoteColor color)
    {
        if (this.FindNote(noteId) is { } location)
        {
            this.ApplyNoteChanges(NoteOperations.Update(this.GetNoteStacks(), location.Note.Update(text, rtf, color)));
        }
    }

    public void MoveNote(Guid noteId, NoteTarget target) =>
        this.ApplyNoteChanges(NoteOperations.Relocate(this.GetNoteStacks(), noteId, target));

    public void DeleteNote(Guid noteId) =>
        this.ApplyNoteChanges(NoteOperations.Delete(this.GetNoteStacks(), noteId));

    private DropStack[] GetNoteStacks() => this.Stacks.Select(static stack => stack.Model).ToArray();

    private void ApplyNoteChanges(IReadOnlyList<DropStack> updated)
    {
        this.BeginCatalogMutation();
        try
        {
            foreach (var model in updated)
            {
                var stack = this.Stacks.SingleOrDefault(stack => stack.Model.Id == model.Id);
                if (stack is null)
                {
                    this.Stacks.Add(this.CreateStackViewModel(model));
                }
                else if (!ReferenceEquals(stack.Model, model))
                {
                    stack.ReplaceModel(model);
                }
            }
        }
        finally
        {
            this.EndCatalogMutation();
        }
    }
}
