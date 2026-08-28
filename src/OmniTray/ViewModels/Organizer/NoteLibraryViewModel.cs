// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Collections.ObjectModel;

namespace OmniTray.ViewModels.Organizer;

public sealed partial class NoteLibraryViewModel(MainViewModel catalog) : ObservableObject, IDisposable
{
    private bool _active;
    private bool _isRefreshing;

    public ObservableCollection<NoteLibraryEntry> Entries { get; } = [];
    [ObservableProperty]
    public partial NoteLibraryEntry? SelectedEntry { get; private set; }

    [ObservableProperty]
    public partial string Summary { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string ErrorMessage { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsErrorOpen { get; set; }

    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanUseCommands))]
    public partial bool IsBusy { get; private set; }

    [ObservableProperty]
    public partial bool CanOpenSelection { get; private set; }

    [ObservableProperty]
    public partial string OpenLabel { get; private set; } = "Open";

    [ObservableProperty]
    public partial bool ShowDeleted { get; set; }

    [ObservableProperty]
    public partial string FilterText { get; set; } = string.Empty;
    public bool CanUseCommands => !this.IsBusy;
    internal Guid? SelectedNoteId => this.SelectedEntry is { IsDeleted: false } entry ? entry.Note.Id : null;
    internal event EventHandler? SelectedNoteChanged;

    partial void OnShowDeletedChanged(bool value) => this.Refresh();
    partial void OnFilterTextChanged(string value) => this.Refresh();

    internal void SetActive(bool active)
    {
        if (this._active == active) { return; }
        this._active = active;
        if (active)
        {
            catalog.CatalogChanged += this.OnCatalogChanged;
            this.Refresh();
        }
        else { catalog.CatalogChanged -= this.OnCatalogChanged; }
    }

    internal void SetSelection(NoteLibraryEntry? entry)
    {
        if (!this._active || this._isRefreshing) { return; }
        this.SelectedEntry = entry;
        this.NotifySelectionChanged();
    }

    private void NotifySelectionChanged()
    {
        this.CanOpenSelection = this.SelectedEntry is not null;
        this.SelectedNoteChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnCatalogChanged(object? sender, EventArgs args) => this.Refresh(keepSelected: true);

    private void Refresh(bool keepSelected = false)
    {
        if (!this._active) { return; }
        var selectedId = this.SelectedEntry?.Note.Id;
        var stacks = catalog.Stacks.Select(stack => stack.Model).ToArray();
        var entries = this.ShowDeleted
            ? catalog.DeletedNotes.Select(entry => new NoteLibraryEntry(entry.Note,
                entry.ItemName is null ? entry.StackName : $"{entry.StackName} · {entry.ItemName}", entry.DeletedAt, true))
            : NoteOperations.Enumerate(stacks).Select(location =>
            {
                var stack = stacks.Single(stack => stack.Id == location.Target.StackId);
                var parent = stack.Items.FirstOrDefault(item => item.Id == location.Target.ItemId);
                var label = parent is null ? "Note in stack" : $"Attached to {parent.DisplayName}";
                return new NoteLibraryEntry(location.Note, $"{stack.Name} · {label}", location.Note.UpdatedAt, false);
            });
        var terms = this.FilterText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var results = entries.Where(entry => (keepSelected && entry.Note.Id == selectedId) || terms.All(term =>
            entry.Note.Text.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            entry.Note.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            entry.Location.Contains(term, StringComparison.OrdinalIgnoreCase))).OrderByDescending(entry => entry.Time).ToArray();

        // Selection changes caused by rebuilding the list must not unload an editor during a local keystroke.
        this._isRefreshing = true;
        try
        {
            this.Entries.Clear();
            foreach (var entry in results) { this.Entries.Add(entry); }
            this.SelectedEntry = this.Entries.FirstOrDefault(entry => entry.Note.Id == selectedId);
        }
        finally { this._isRefreshing = false; }
        this.NotifySelectionChanged();
        this.OpenLabel = this.ShowDeleted ? "Restore" : "Open";
        this.Summary = $"{results.Length} notes. " + (this.ShowDeleted
            ? "Kept until permanently deleted. Restore returns a note to its owner when available, or to a stack item."
            : "All placements, most recently edited first.");
    }

    internal Task OpenAsync(NoteLibraryEntry entry) => this.RunAsync(async () =>
    {
        var note = entry.IsDeleted ? catalog.RestoreDeletedNote(entry.Note.Id) : entry.Note;
        App.Current.ShowNote(note.Id);
        if (entry.IsDeleted) { await App.Current.SaveNotesAsync(); }
    });

    internal Task PurgeAsync(NoteLibraryEntry entry, Func<Task<bool>> confirm) => this.RunAsync(async () =>
    {
        if (entry.IsDeleted && await confirm()) { await App.Current.PurgeDeletedNoteAsync(entry.Note.Id); }
    });

    private async Task RunAsync(Func<Task> operation)
    {
        if (this.IsBusy || !this._active) { return; }
        this.IsBusy = true;
        this.IsErrorOpen = false;
        try { await operation(); }
        catch (Exception exception)
        {
            this.ErrorMessage = exception.Message;
            this.IsErrorOpen = true;
        }
        finally { this.IsBusy = false; }
    }

    public void Dispose()
    {
        this.SetActive(false);
        this.SelectedEntry = null;
        this.Entries.Clear();
    }
}
