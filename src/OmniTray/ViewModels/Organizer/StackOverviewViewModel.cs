// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

using System.Collections.ObjectModel;

namespace OmniTray.ViewModels.Organizer;

public sealed partial class StackOverviewViewModel(MainViewModel catalog) : ObservableObject
{
    internal event EventHandler? ScopeCommandCompleted;
    public ObservableCollection<DropStackViewModel> VisibleStacks { get; } = [];
    public IReadOnlyList<DropStackViewModel> SelectedStacks { get; private set; } = [];
    public EdgeShelfSide? ScopeSide { get; internal set; }
    public string FilterText { get; internal set; } = string.Empty;
    public StackOrganizerSortMode SortMode { get; internal set; }
    public StackOrganizerLayoutMode LayoutMode { get; internal set; } = StackOrganizerLayoutMode.Medium;
    internal bool IsApplyingScopeCommand { get; private set; }

    [ObservableProperty]
    public partial string Title { get; private set; } = "All stacks";

    [ObservableProperty]
    public partial string Summary { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string EmptyDescription { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string SelectionSummary { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsEmpty { get; private set; }

    [ObservableProperty]
    public partial bool HasScope { get; private set; }

    [ObservableProperty]
    public partial bool CanOpenScope { get; private set; }

    public bool CanOpenSelection => this.SelectedStacks.Count == 1;
    public bool HasSelection => this.SelectedStacks.Count > 0;

    public bool CanReorder =>
        this.SortMode == StackOrganizerSortMode.Manual && string.IsNullOrWhiteSpace(this.FilterText);

    internal void Refresh()
    {
        IEnumerable<DropStackViewModel> source = this.ScopeSide is { } side
            ? catalog.GetEdgeStacks(side)
            : catalog.Stacks;
        var query = this.FilterText.Trim();
        if (query.Length > 0) { source = source.Where(stack => StackFilter.Matches(stack.Model, query)); }

        source = this.SortMode switch
        {
            StackOrganizerSortMode.Name => source.OrderBy(static stack => stack.Name,
                StringComparer.CurrentCultureIgnoreCase),
            StackOrganizerSortMode.ItemCount => source.OrderByDescending(static stack => stack.Model.Items.Count)
                .ThenBy(static stack => stack.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => source
        };

        var visible = source.ToArray();
        if (!this.VisibleStacks.SequenceEqual(visible))
        {
            this.VisibleStacks.Clear();
            foreach (var stack in visible) { this.VisibleStacks.Add(stack); }
        }

        this.IsEmpty = visible.Length == 0;
        this.EmptyDescription = query.Length > 0
            ? "No stacks match this filter."
            : this.ScopeSide is { } scopeSide
                ? $"No stacks are assigned to the {scopeSide.GetDisplayName().ToLowerInvariant()} edge. Create one here or move an existing stack to this edge."
                : "Create a stack to start organizing captured content.";
        this.RefreshHeader(visible.Length);
        this.SetSelection(this.SelectedStacks.Where(visible.Contains).ToArray());
    }

    internal void SetSelection(IReadOnlyList<DropStackViewModel> stacks)
    {
        this.SelectedStacks = stacks;
        this.OnPropertyChanged(nameof(this.CanOpenSelection));
        this.OnPropertyChanged(nameof(this.HasSelection));
        this.SelectionSummary = stacks.Count == 0
            ? this.VisibleStacks.Count == 1 ? "1 stack" : $"{this.VisibleStacks.Count} stacks"
            : stacks.Count == 1
                ? "1 stack selected"
                : $"{stacks.Count} stacks selected";
    }

    private void RefreshHeader(int visibleCount)
    {
        this.HasScope = this.ScopeSide is not null;
        this.CanOpenScope = this.ScopeSide is { } scope && catalog.IsEdgeWindowEnabled(scope);
        if (this.ScopeSide is not { } side)
        {
            this.Title = "All stacks";
            this.Summary = visibleCount == 1 ? "1 stack" : $"{visibleCount} stacks";
            return;
        }

        var source = EdgeContentSharingPolicy.ResolveContentSource(side,
            catalog.SyncLeftAndRightEdgeContent, catalog.SyncTopAndBottomEdgeContent, catalog.SyncAllEdgeContent);
        this.Title = $"{side.GetDisplayName()} edge";
        this.Summary = this.CanOpenScope
            ? source == side
                ? visibleCount == 1 ? "1 stack on this edge" : $"{visibleCount} stacks on this edge"
                : $"Shared with the {source.GetDisplayName().ToLowerInvariant()} edge · {visibleCount} {(visibleCount == 1 ? "stack" : "stacks")}"
            : "This edge window is disabled in Settings.";
    }

    internal int AssignSelectionToEdge(EdgeShelfSide? side)
    {
        var stacks = this.SelectedStacks.ToArray();
        this.IsApplyingScopeCommand = true;
        var changedCount = 0;
        try
        {
            foreach (var stack in stacks)
            {
                if (side is { } edge ? catalog.AssignStackToEdge(stack, edge) : catalog.RemoveStackFromEdge(stack))
                {
                    changedCount++;
                }
            }
        }
        finally
        {
            this.IsApplyingScopeCommand = false;
            this.ScopeCommandCompleted?.Invoke(this, EventArgs.Empty);
        }

        return changedCount;
    }
}

public enum StackOrganizerSortMode { Manual, Name, ItemCount }

public enum StackOrganizerLayoutMode { Compact, Medium, Large }
