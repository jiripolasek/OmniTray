// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

namespace OmniTray.ViewModels.Organizer;

public sealed partial class StackContentsViewModel(MainViewModel catalog) : ObservableObject
{
    [ObservableProperty]
    public partial DropStackViewModel? Stack { get; private set; }

    [ObservableProperty]
    public partial string Title { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Summary { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string SelectionSummary { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string BackLabel { get; private set; } = "Back to stacks";
    public DropItemViewModel? SelectedItem { get; private set; }
    public int SelectedItemCount { get; private set; }

    internal void SetStack(DropStackViewModel? stack, bool fromSearch = false)
    {
        this.Stack = stack;
        this.BackLabel = fromSearch ? "Back to search results" : "Back to stacks";
        this.SetSelection(null, 0);
    }

    internal void SetSelection(DropItemViewModel? item, int count)
    {
        this.SelectedItem = item;
        this.SelectedItemCount = count;
        this.Refresh();
    }

    internal void Refresh()
    {
        this.Title = this.Stack?.Name ?? string.Empty;
        this.Summary = this.Stack is { } stack ? $"{stack.ItemCountText} · {stack.EdgePlacementText}" : string.Empty;
        this.SelectionSummary = this.Stack is null ? string.Empty
            : this.SelectedItemCount == 0 ? this.Stack.ItemCountText
            : this.SelectedItemCount == 1 ? "1 item selected" : $"{this.SelectedItemCount} items selected";
    }

    internal bool AssignToEdge(EdgeShelfSide? side) => this.Stack is { } stack &&
        (side is { } edge ? catalog.AssignStackToEdge(stack, edge) : catalog.RemoveStackFromEdge(stack));

    internal void ChangeViewMode(StackInspectorViewMode viewMode)
    {
        if (this.Stack is { } stack && stack.InspectorViewMode != viewMode) { stack.ChangeInspectorViewMode(viewMode); }
    }
}
