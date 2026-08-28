// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using Windows.ApplicationModel.DataTransfer;
using OmniTray.ViewModels.Organizer;

namespace OmniTray.ViewModels;

public sealed class StackOrganizerViewModel
{
    public StackOrganizerViewModel(MainViewModel catalog)
    {
        this.Catalog = catalog;
        this.Overview = new(catalog);
        this.Stack = new(catalog);
        this.Search = new(catalog);
        this.RefreshScopes();
    }

    internal MainViewModel Catalog { get; }
    public StackOrganizerNavigationState Navigation { get; } = new();
    public StackOverviewViewModel Overview { get; }
    public StackContentsViewModel Stack { get; }
    public StackSearchViewModel Search { get; }

    public IReadOnlyList<StackOrganizerScopeViewModel> Scopes { get; } =
    [
        new(null, "All stacks"),
        new(EdgeShelfSide.Left, "Left edge"),
        new(EdgeShelfSide.Right, "Right edge"),
        new(EdgeShelfSide.Top, "Top edge"),
        new(EdgeShelfSide.Bottom, "Bottom edge")
    ];

    public StackOrganizerScopeViewModel AllStacksScope => this.Scopes[0];
    public StackOrganizerScopeViewModel LeftEdgeScope => this.Scopes[1];
    public StackOrganizerScopeViewModel RightEdgeScope => this.Scopes[2];
    public StackOrganizerScopeViewModel TopEdgeScope => this.Scopes[3];
    public StackOrganizerScopeViewModel BottomEdgeScope => this.Scopes[4];

    internal void RefreshScopes()
    {
        this.Scopes[0].UpdateStatus(
            this.Catalog.Stacks.Count == 1 ? "1 stack" : $"{this.Catalog.Stacks.Count} stacks");
        foreach (var scope in this.Scopes.Where(static scope => scope.Side is not null))
        {
            var side = scope.Side!.Value;
            var source = EdgeContentSharingPolicy.ResolveContentSource(
                side,
                this.Catalog.SyncLeftAndRightEdgeContent,
                this.Catalog.SyncTopAndBottomEdgeContent,
                this.Catalog.SyncAllEdgeContent);
            var stackCount = this.Catalog.GetEdgeStacks(side).Count;
            var enabled = this.Catalog.IsEdgeWindowEnabled(side);
            var countText = stackCount == 1 ? "1 stack" : $"{stackCount} stacks";
            scope.UpdateStatus(
                enabled
                    ? source == side ? countText : $"Shared · {countText}"
                    : "Disabled");
        }
    }

    internal async Task<DropStackViewModel?> CreateStackFromItemDropAsync(DataPackageView dataView, bool copy)
    {
        // Resolve private item identity before public formats, and suppress external-move cleanup.
        var reference = await DragDropDataService.ReadItemReferenceAsync(dataView);
        var source = reference is null
            ? null
            : this.Catalog.Stacks.FirstOrDefault(stack => stack.Model.Id == reference.SourceStackId);
        if (reference is null || source is null)
        {
            App.Current.ShowToast("Those items are no longer available.", InfoBarSeverity.Warning);
            return null;
        }

        var selectedIds = reference.ItemIds.ToHashSet();
        var items = source.Model.Items.Where(item => selectedIds.Contains(item.Id)).ToArray();
        if (items.Length != selectedIds.Count)
        {
            App.Current.ShowToast("Those items are no longer available.", InfoBarSeverity.Warning);
            return null;
        }

        var name = items.Length == 1 ? items[0].DisplayName : $"{items.Length} items";
        var created = this.Catalog.AddStack(DropStack.CreateEmpty(name, source.Tint));
        try
        {
            if (await App.Current.TransferItemsAsync(reference, created, 0, copy))
            {
                return created;
            }

            App.Current.ShowToast("The drop did not create a stack.", InfoBarSeverity.Warning);
            return null;
        }
        finally
        {
            if (created.Model.Items.Count == 0)
            {
                this.Catalog.RemoveStack(created);
            }
        }
    }

}
