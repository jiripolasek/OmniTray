// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Collections.ObjectModel;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace OmniTray.Views;

public sealed class NoteLibraryEntry(StickyNote note, string location, DateTimeOffset time, bool isDeleted)
{
    public StickyNote Note { get; } = note;
    public string Location { get; } = location;
    public DateTimeOffset Time { get; } = time;
    public bool IsDeleted { get; } = isDeleted;
    public string Preview => this.Note.Text.Length > 220 ? this.Note.Text[..220] + "…" : this.Note.Text;
    public string TimeText => $"{(this.IsDeleted ? "Deleted" : "Updated")} {this.Time.ToLocalTime():g}";
}

public sealed partial class NoteLibraryPage : Page
{
    private readonly MainViewModel _catalog;
    private readonly Window _owner;
    private bool _ready;
    private bool _busy;
    private bool _isRefreshing;

    internal NoteLibraryPage(MainViewModel catalog, Window owner)
    {
        this._catalog = catalog;
        this._owner = owner;
        this.InitializeComponent();
    }

    internal void SetActive(bool active)
    {
        if (this._ready == active) { return; }
        this._ready = active;
        if (active)
        {
            this._catalog.CatalogChanged += this.OnCatalogChanged;
            this.Commands.IsEnabled = !this._busy;
            this.Refresh();
        }
        else
        {
            this._catalog.CatalogChanged -= this.OnCatalogChanged;
        }
    }

    public ObservableCollection<NoteLibraryEntry> Entries { get; } = [];

    internal event EventHandler? SelectedNoteChanged;
    internal Guid? SelectedNoteId => this.NotesList.SelectedItem is NoteLibraryEntry { IsDeleted: false } entry ? entry.Note.Id : null;
    internal void FocusList() => this.NotesList.Focus(FocusState.Keyboard);

    internal void ShowDeleted(bool deleted)
    {
        this.ModeBox.SelectedIndex = deleted ? 1 : 0;
        this.Refresh();
    }

    private void OnCatalogChanged(object? sender, EventArgs args) => this.Refresh(keepSelected: true);
    private void OnModeChanged(object sender, SelectionChangedEventArgs args) => this.Refresh();
    private void OnSearchChanged(object sender, TextChangedEventArgs args) => this.Refresh();
    private void OnSelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (this._ready && !this._isRefreshing) { this.NotifySelectionChanged(); }
    }

    private void NotifySelectionChanged()
    {
        this.OpenButton.IsEnabled = this.NotesList.SelectedItem is NoteLibraryEntry;
        this.DeleteButton.IsEnabled = this.OpenButton.IsEnabled;
        this.SelectedNoteChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Refresh(bool keepSelected = false)
    {
        if (!this._ready)
        {
            return;
        }
        var selectedId = (this.NotesList.SelectedItem as NoteLibraryEntry)?.Note.Id;
        var deleted = this.ModeBox.SelectedIndex == 1;
        var stacks = this._catalog.Stacks.Select(stack => stack.Model).ToArray();
        var entries = deleted
            ? this._catalog.DeletedNotes.Select(entry => new NoteLibraryEntry(entry.Note,
                entry.ItemName is null ? entry.StackName : $"{entry.StackName} · {entry.ItemName}", entry.DeletedAt, true))
            : NoteOperations.Enumerate(stacks).Select(location =>
            {
                var stack = stacks.Single(stack => stack.Id == location.Target.StackId);
                var parent = stack.Items.FirstOrDefault(item => item.Id == location.Target.ItemId);
                var label = parent is null ? "Note in stack" : $"Attached to {parent.DisplayName}";
                return new NoteLibraryEntry(location.Note, $"{stack.Name} · {label}", location.Note.UpdatedAt, false);
            });
        var terms = this.SearchBox.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var results = entries.Where(entry => (keepSelected && entry.Note.Id == selectedId) || terms.All(term => entry.Note.Text.Contains(term, StringComparison.OrdinalIgnoreCase)
            || entry.Note.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase)
            || entry.Location.Contains(term, StringComparison.OrdinalIgnoreCase))).OrderByDescending(entry => entry.Time).ToArray();
        // Rebuilding results must not unload the editor midway through a local keystroke.
        this._isRefreshing = true;
        try
        {
            this.Entries.Clear();
            foreach (var entry in results) { this.Entries.Add(entry); }
            this.NotesList.SelectedItem = this.Entries.FirstOrDefault(entry => entry.Note.Id == selectedId);
        }
        finally { this._isRefreshing = false; }
        this.NotifySelectionChanged();
        this.OpenButton.Label = deleted ? "Restore" : "Open";
        this.DeleteButton.Visibility = deleted ? Visibility.Visible : Visibility.Collapsed;
        this.SummaryText.Text = $"{results.Length} notes. " + (deleted
            ? "Kept until permanently deleted. Restore returns a note to its owner when available, or to a stack item."
            : "All placements, most recently edited first.");
    }

    private void OnNewClick(object sender, RoutedEventArgs args) => App.Current.CreateQuickNote();
    private async void OnClipboardClick(object sender, RoutedEventArgs args) => await App.Current.CreateClipboardNoteAsync();
    private async void OnOpenClick(object sender, RoutedEventArgs args)
    {
        if (this.NotesList.SelectedItem is NoteLibraryEntry entry) { await this.OpenAsync(entry); }
    }
    private async void OnNotesDoubleTapped(object sender, DoubleTappedRoutedEventArgs args)
    {
        var element = args.OriginalSource as DependencyObject;
        while (element is not null && element != this.NotesList)
        {
            if (element is FrameworkElement { DataContext: NoteLibraryEntry entry })
            {
                args.Handled = true;
                await this.OpenAsync(entry);
                return;
            }
            element = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(element);
        }
    }

    private async void OnNotesKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key == VirtualKey.Enter && this.NotesList.SelectedItem is NoteLibraryEntry entry)
        {
            args.Handled = true;
            await this.OpenAsync(entry);
        }
    }

    private async Task OpenAsync(NoteLibraryEntry entry)
    {
        if (this._busy) { return; }
        this._busy = true;
        this.Commands.IsEnabled = false;
        this.ErrorBar.IsOpen = false;
        try
        {
            var note = entry.IsDeleted ? this._catalog.RestoreDeletedNote(entry.Note.Id) : entry.Note;
            App.Current.ShowNote(note.Id);
            if (entry.IsDeleted) { await App.Current.SaveNotesAsync(); }
        }
        catch (Exception exception) { this.ShowError(exception); }
        finally { this._busy = false; if (this._ready) { this.Commands.IsEnabled = true; } }
    }

    private async void OnPurgeClick(object sender, RoutedEventArgs args)
    {
        if (this._busy || this.NotesList.SelectedItem is not NoteLibraryEntry { IsDeleted: true } entry) { return; }
        this._busy = true;
        this.Commands.IsEnabled = false;
        this.ErrorBar.IsOpen = false;
        try
        {
            if (await StackDialogWindow.ShowAsync(this._owner, "Permanently delete note?",
                "This deletes the note and its recovery history. This cannot be undone.", "Delete permanently"))
            {
                await App.Current.PurgeDeletedNoteAsync(entry.Note.Id);
            }
        }
        catch (Exception exception) { this.ShowError(exception); }
        finally { this._busy = false; if (this._ready) { this.Commands.IsEnabled = true; } }
    }

    private void ShowError(Exception exception)
    {
        this.ErrorBar.Message = exception.Message;
        this.ErrorBar.IsOpen = true;
    }
}
