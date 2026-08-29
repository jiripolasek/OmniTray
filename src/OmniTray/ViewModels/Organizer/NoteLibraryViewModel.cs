// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

using System.Collections.ObjectModel;

namespace OmniTray.ViewModels.Organizer;

public sealed partial class NoteLibraryViewModel(MainViewModel catalog) : ObservableObject, IDisposable
{
    internal event EventHandler? SelectedNoteChanged;
    private bool _active;
    private bool _isRefreshing;

    public ObservableCollection<NoteLibraryEntry> Entries { get; } = [];
    public IReadOnlyList<NoteLibraryEntry> SelectedEntries { get; private set; } = [];
    public OrganizerCollectionViewMode LayoutMode { get; internal set; } = OrganizerCollectionViewMode.List;

    [ObservableProperty]
    public partial string Summary { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string ErrorMessage { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsErrorOpen { get; set; }

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanUseCommands))]
    public partial bool IsBusy { get; private set; }

    [ObservableProperty]
    public partial bool CanOpenSelection { get; private set; }

    [ObservableProperty]
    public partial bool CanGoToSelection { get; private set; }

    [ObservableProperty]
    public partial bool HasSelection { get; private set; }

    [ObservableProperty]
    public partial string SelectionSummary { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string OpenLabel { get; private set; } = "Open";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DeleteLabel))]
    public partial bool ShowDeleted { get; set; }

    [ObservableProperty]
    public partial string FilterText { get; set; } = string.Empty;

    public bool CanUseCommands => !this.IsBusy;
    public string DeleteLabel => this.ShowDeleted ? "Delete permanently" : "Delete";
    internal Guid? SelectedNoteId => this.SelectedEntries is [{ IsDeleted: false } entry] ? entry.Note.Id : null;

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

    internal void SetSelection(IReadOnlyList<NoteLibraryEntry> entries)
    {
        if (!this._active || this._isRefreshing) { return; }

        this.ApplySelection(entries);
    }

    private void ApplySelection(IEnumerable<NoteLibraryEntry> entries)
    {
        this.SelectedEntries = entries
            .Where(this.Entries.Contains)
            .DistinctBy(static entry => entry.Note.Id)
            .ToArray();
        this.HasSelection = this.SelectedEntries.Count > 0;
        this.CanOpenSelection = this.ShowDeleted
            ? this.HasSelection
            : this.SelectedEntries.Count == 1;
        this.CanGoToSelection = this.SelectedEntries is [{ CanGoToStack: true }];
        this.SelectionSummary = this.SelectedEntries.Count switch
        {
            0 => string.Empty,
            1 => "1 note selected",
            _ => $"{this.SelectedEntries.Count} notes selected"
        };
        this.SelectedNoteChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnCatalogChanged(object? sender, EventArgs args) => this.Refresh(true);

    private void Refresh(bool keepSelected = false)
    {
        if (!this._active) { return; }

        var selectedIds = keepSelected
            ? this.SelectedEntries.Select(static entry => entry.Note.Id).ToHashSet()
            : [];
        var stacks = catalog.Stacks.Select(stack => stack.Model).ToArray();
        var entries = this.ShowDeleted
            ? catalog.DeletedNotes.Select(entry => new NoteLibraryEntry(entry.Note, entry.Target,
                entry.ItemName is null ? entry.StackName : $"{entry.StackName} · {entry.ItemName}", entry.DeletedAt,
                true))
            : NoteOperations.Enumerate(stacks).Select(location =>
            {
                var stack = stacks.Single(stack => stack.Id == location.Target.StackId);
                var parent = stack.Items.FirstOrDefault(item => item.Id == location.Target.ItemId);
                var label = parent is null ? "Note in stack" : $"Attached to {parent.DisplayName}";
                return new NoteLibraryEntry(
                    location.Note,
                    location.Target,
                    $"{stack.Name} · {label}",
                    location.Note.UpdatedAt,
                    false);
            });
        var terms = this.FilterText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var results = entries.Where(entry => selectedIds.Contains(entry.Note.Id) || terms.All(term =>
                entry.Note.Text.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                entry.Note.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                entry.Location.Contains(term, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(entry => entry.Time)
            .ToArray();

        // Selection changes caused by rebuilding the list must not unload an editor during a local keystroke.
        this._isRefreshing = true;
        try
        {
            this.Entries.Clear();
            foreach (var entry in results) { this.Entries.Add(entry); }

        }
        finally { this._isRefreshing = false; }

        this.OpenLabel = this.ShowDeleted ? "Restore" : "Open";
        this.ApplySelection(this.Entries.Where(entry => selectedIds.Contains(entry.Note.Id)));
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

    internal Task OpenSelectionAsync(IReadOnlyList<NoteLibraryEntry> entries)
    {
        var selected = entries
            .DistinctBy(static entry => entry.Note.Id)
            .ToArray();
        if (selected is [var entry] && !entry.IsDeleted)
        {
            return this.OpenAsync(entry);
        }

        if (selected.Length == 0 || selected.Any(static entry => !entry.IsDeleted))
        {
            return Task.CompletedTask;
        }

        return this.RunAsync(async () =>
        {
            var restored = selected.Select(entry => catalog.RestoreDeletedNote(entry.Note.Id)).ToArray();
            await App.Current.SaveNotesAsync();
            if (restored is [var note])
            {
                App.Current.ShowNote(note.Id);
            }
        });
    }

    internal Task ChangeColorAsync(NoteLibraryEntry entry, NoteColor color) => this.RunAsync(async () =>
    {
        if (entry.IsDeleted || !this.Entries.Contains(entry) || entry.Note.Color == color)
        {
            return;
        }

        catalog.UpdateNote(entry.Note.Id, entry.Note.Text, entry.Note.Rtf, color);
        await App.Current.SaveNotesAsync();
    });

    internal Task DeleteAsync(IReadOnlyList<NoteLibraryEntry> entries, Func<Task<bool>> confirm) => this.RunAsync(async () =>
    {
        var selected = entries
            .DistinctBy(static entry => entry.Note.Id)
            .ToArray();
        if (selected.Length == 0)
        {
            return;
        }

        if (!await confirm())
        {
            return;
        }

        foreach (var entry in selected.Where(static entry => !entry.IsDeleted))
        {
            catalog.DeleteNote(entry.Note.Id);
        }

        if (selected.Any(static entry => !entry.IsDeleted))
        {
            await App.Current.SaveNotesAsync();
        }

        foreach (var entry in selected.Where(static entry => entry.IsDeleted))
        {
            await App.Current.PurgeDeletedNoteAsync(entry.Note.Id);
        }
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
        this.SelectedEntries = [];
        this.Entries.Clear();
    }
}
