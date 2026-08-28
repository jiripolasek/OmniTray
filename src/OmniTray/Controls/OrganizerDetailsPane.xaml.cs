// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

namespace OmniTray.Controls;

// The editor stays alive across page changes so pending saves and retry state are not lost.
public sealed partial class OrganizerDetailsPane : UserControl, IDisposable
{
    public OrganizerDetailsPane() => this.InitializeComponent();

    public OrganizerDetailsViewModel ViewModel { get; } = new();
    internal NoteEditorPane NoteEditor => this.InlineNoteEditor;

    internal void ShowEmpty(string title, string description)
    {
        this.ViewModel.EmptyTitle = title;
        this.ViewModel.EmptyDescription = description;
        this.ShowItem(null, null, 0);
    }

    internal void ShowItem(DropStackViewModel? stack, DropItemViewModel? item, int selectedCount)
    {
        this.InlineNoteEditor.SetNote(selectedCount == 1 ? item?.Model.Note?.Id : null);
        this.ViewModel.SetItem(stack, this.InlineNoteEditor.NoteId is null ? item : null);
        this.RefreshVisibility();
    }

    internal void ShowNote(Guid? noteId)
    {
        this.InlineNoteEditor.SetNote(noteId);
        this.ViewModel.SetItem(null, null);
        this.RefreshVisibility();
    }

    private void RefreshVisibility()
    {
        var hasNote = this.InlineNoteEditor.NoteId is not null;
        this.InlineNoteEditor.Visibility = hasNote ? Visibility.Visible : Visibility.Collapsed;
        this.DetailsEmptyState.Visibility = !hasNote && this.ViewModel.Item is null ? Visibility.Visible : Visibility.Collapsed;
        this.DetailsScrollViewer.Visibility = !hasNote && this.ViewModel.Item is not null ? Visibility.Visible : Visibility.Collapsed;
    }

    public void Dispose()
    {
        this.InlineNoteEditor.Dispose();
        this.ViewModel.SetItem(null, null);
    }
}
