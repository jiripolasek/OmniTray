// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Diagnostics;

namespace OmniTray;

public partial class App
{
    internal void ShowNote(Guid noteId) => this.RunOnUiThread(() => this._windows?.ShowNote(noteId));

    internal void ShowNotes(bool deleted = false) => this.RunOnUiThread(() => this._windows?.ShowNotes(deleted));

    internal void ShowNoteOwner(Guid stackId, Guid? itemId = null) =>
        this.RunOnUiThread(() => this._windows?.ShowNoteOwner(stackId, itemId));

    private DropStackViewModel ResolveQuickNoteStack(Guid? stackId)
    {
        if (stackId is { } id)
        {
            return this.StackCatalogViewModel.Stacks.FirstOrDefault(stack => stack.Model.Id == id)
                ?? throw new ArgumentException("That stack is no longer available.");
        }
        return this.StackCatalogViewModel.Stacks.FirstOrDefault(stack => stack.Name == "Inbox")
            ?? this.StackCatalogViewModel.AddStack(DropStack.CreateEmpty("Inbox"));
    }

    internal void CreateQuickNote(Guid? stackId = null)
    {
        try
        {
            this.CreateNote(new NoteTarget(this.ResolveQuickNoteStack(stackId).Model.Id, NotePlacement.StackItem));
        }
        catch (Exception exception) { this.ShowToast(exception.Message, InfoBarSeverity.Error); }
    }

    internal async Task CreateClipboardNoteAsync(Guid? stackId = null)
    {
        try
        {
            var content = await NoteClipboardService.ReadAsync();
            var stack = this.ResolveQuickNoteStack(stackId);
            var note = this.StackCatalogViewModel.CreateNote(new NoteTarget(stack.Model.Id, NotePlacement.StackItem),
                content.Text, content.Rtf);
            this.ShowNote(note.Id);
        }
        catch (Exception exception) { this.ShowToast(exception.Message, InfoBarSeverity.Error); }
    }

    internal void CreateNote(NoteTarget target)
    {
        try
        {
            var note = this.StackCatalogViewModel.CreateNote(target);
            this.ShowNote(note.Id);
        }
        catch (ArgumentException exception)
        {
            this.ShowToast(exception.Message, InfoBarSeverity.Warning);
        }
    }

    internal Task SaveNotesAsync() => this.SaveCatalogNowAsync();

    internal async Task ConvertTextToNoteAsync(Guid stackId, Guid itemId, bool duplicate)
    {
        var source = this.StackCatalogViewModel.Stacks.SingleOrDefault(stack => stack.Model.Id == stackId)
            ?.Model.Items.SingleOrDefault(item => item.Id == itemId && item.Kind == DropItemKind.Text)
            ?? throw new ArgumentException("The text item is no longer available.", nameof(itemId));
        string? plainText = null;
        if (source.Text is null && !string.IsNullOrWhiteSpace(source.Rtf))
        {
            // Read the plain-text companion without reserializing or altering the captured RTF.
            plainText = NoteClipboardService.ReadRtfText(source.Rtf);
        }

        var note = this.StackCatalogViewModel.ConvertTextToNote(stackId, itemId, duplicate, plainText);
        this.ShowNote(note.Id);
        try
        {
            // The history retains the original capture, including its files, for undo and provenance.
            await this.SaveCatalogNowAsync();
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"OmniTray could not save the converted note: {exception}");
            this.ShowToast("The note is open, but saving failed. Original captured files were kept. Use Save now in the note to retry.",
                InfoBarSeverity.Error);
        }
    }

    private async Task DeleteUnreferencedCapturesAsync(IEnumerable<DropItem> items, bool reportFailureToCaller = false)
    {
        // A save failure must leave any files needed by the last durable catalog intact.
        try
        {
            await this.SaveCatalogNowAsync();
        }
        catch (Exception exception) when (!reportFailureToCaller)
        {
            Debug.WriteLine($"OmniTray could not save removal: {exception}");
            this.ShowToast("The removal could not be saved. Captured files were kept. Try saving again before exiting.", InfoBarSeverity.Error);
            return;
        }
        var retained = this.StackCatalogViewModel.NoteHistory.Select(history => history.SourceItem)
            .Concat(this.StackCatalogViewModel.Stacks.SelectMany(stack => stack.Model.Items));
        await ContentStore.DeleteOwnedAsync(items, retained);
    }

    internal async Task PurgeDeletedNoteAsync(Guid noteId)
    {
        var deleted = this.StackCatalogViewModel.DeletedNotes.FirstOrDefault(entry => entry.Note.Id == noteId);
        if (deleted is null) { return; }
        var history = this.StackCatalogViewModel.NoteHistory.Where(entry => entry.NoteId == noteId).ToArray();
        this.StackCatalogViewModel.PermanentlyDeleteNote(noteId);
        try
        {
            await this.DeleteUnreferencedCapturesAsync(history.Select(entry => entry.SourceItem), true);
        }
        catch
        {
            this.StackCatalogViewModel.RestoreRecoveryEntry(deleted, history);
            throw;
        }
    }
}
