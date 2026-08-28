// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Input;

namespace OmniTray.Views;

public sealed partial class StackOrganizerWindow
{
    private NoteLibraryPage? _notesPage;
    private bool _isShowingNotes;
    private bool _isOrganizerClosed;
    private bool _isClosingWithNoteChanges;
    private bool _allowOrganizerClose;

    internal void SelectNotes(bool deleted)
    {
        this.OrganizerNavigation.SelectedItem = this.NotesNavigationItem;
        this.ShowNotesPage();
        this._notesPage!.ShowDeleted(deleted);
        if (this.AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
        {
            presenter.Restore();
        }
        this.Activate();
    }

    private void ShowNotesPage()
    {
        if (this._isShowingNotes) { return; }
        // Clear the outgoing stack and details immediately, just as the search page does.
        this.ShowOverview();
        this._scopeSide = null;
        this._isShowingNotes = true;
        if (this._notesPage is null)
        {
            this._notesPage = new NoteLibraryPage(this._viewModel, this);
            this._notesPage.SelectedNoteChanged += this.OnLibraryNoteSelected;
        }
        this.NotesContent.Content = this._notesPage;
        this.OverviewContent.Visibility = Visibility.Collapsed;
        this.StackContent.Visibility = Visibility.Collapsed;
        this.BrowserContent.Visibility = Visibility.Visible;
        this.DetailsEmptyTitleText.Text = "Select a note";
        this.DetailsEmptyDescriptionText.Text = "Edit it here or open it in a window. Restore deleted notes before editing.";
        this.NotesContent.Visibility = Visibility.Visible;
        this._notesPage.SetActive(true);
        this.RefreshDetailsPane();
    }

    private void LeaveNotesPage()
    {
        this._isShowingNotes = false;
        this._notesPage?.SetActive(false);
        this.NotesContent.Visibility = Visibility.Collapsed;
        // Keep the page instance (filter, mode, selection), but detach its visual tree
        // and catalog subscription while another organizer page is showing.
        this.NotesContent.Content = null;
        this.InlineNoteEditor.SetNote(null);
        this.InlineNoteEditor.Visibility = Visibility.Collapsed;
    }

    private void OnLibraryNoteSelected(object? sender, EventArgs args)
    {
        if (this._isShowingNotes) { this.RefreshDetailsPane(); }
    }

    private void OnNoteSaveStateChanged(object? sender, EventArgs args)
    {
        if (this._isOrganizerClosed) { return; }
        this.NoteSaveErrorBar.IsOpen = this.InlineNoteEditor.LastSaveError is not null;
        this.NoteSaveErrorBar.Message = this.InlineNoteEditor.LastSaveError is { } error
            ? $"Changes are still in memory. Retry before closing. {error.Message}" : string.Empty;
    }

    private async void OnRetryNoteSaveClick(object sender, RoutedEventArgs args) => await this.InlineNoteEditor.SaveAsync();

    private async void OnSaveNoteInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (this.InlineNoteEditor.NoteId is null && !this.InlineNoteEditor.HasUnsavedChanges) { return; }
        args.Handled = true;
        await this.InlineNoteEditor.SaveAsync();
    }

    private void OnSwitchNotePreviewInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (this.BrowserContent.Visibility != Visibility.Visible || this.DetailsPane.Visibility != Visibility.Visible
            || this.InlineNoteEditor.NoteId is null) { return; }
        if (this.InlineNoteEditor.HasEditingFocus)
        {
            if (this._isShowingNotes) { this._notesPage?.FocusList(); }
            else { this.ItemsOrganizer.FocusItemList(); }
        }
        else { this.InlineNoteEditor.FocusText(); }
        args.Handled = true;
    }

    private void OnOrganizerActivated(object sender, WindowActivatedEventArgs args) =>
        this.InlineNoteEditor.SetHostActive(args.WindowActivationState != WindowActivationState.Deactivated);

    internal void SetNoteEditingEnabled(bool enabled) =>
        this.InlineNoteEditor.SetEditingEnabled(enabled && !this._isClosingWithNoteChanges);

    private async void OnOrganizerClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (this._allowOrganizerClose || !this.InlineNoteEditor.HasUnsavedChanges) { return; }
        args.Cancel = true;
        if (this._isClosingWithNoteChanges) { return; }
        this._isClosingWithNoteChanges = true;
        this.SetNoteEditingEnabled(false);
        if (await this.InlineNoteEditor.SaveAsync())
        {
            if (!this._isOrganizerClosed)
            {
                this._allowOrganizerClose = true;
                this.Close();
            }
        }
        else if (!this._isOrganizerClosed)
        {
            this._isClosingWithNoteChanges = false;
            this.SetNoteEditingEnabled(true);
        }
    }
}
