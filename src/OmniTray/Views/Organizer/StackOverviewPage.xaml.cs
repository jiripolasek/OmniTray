// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using Windows.ApplicationModel.DataTransfer;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using OmniTray.Controls;
using OmniTray.ViewModels.Organizer;

namespace OmniTray.Views.Organizer;

public sealed partial class StackOverviewPage : Page
{
    private readonly MainViewModel _catalog;
    private readonly ListInsertionAdornerController _stackInsertionAdorner;
    private readonly PointerEventHandler _stackPointerMovedHandler;
    private bool _isStackDragOperationActive;
    private bool _isRefreshing;

    internal StackOverviewPage(MainViewModel catalog, StackOverviewViewModel viewModel)
    {
        this._catalog = catalog;
        this.ViewModel = viewModel;
        this._stackPointerMovedHandler = this.OnStackPointerMoved;
        this.InitializeComponent();
        this._stackInsertionAdorner = new(this.StackGrid, "StackInsertionAdorner", Orientation.Horizontal);
        this._stackInsertionAdorner.SetLayout(Orientation.Horizontal, true);
        this.ApplyOverviewLayout(this.ViewModel.LayoutMode);
    }

    public StackOverviewViewModel ViewModel { get; }
    internal event EventHandler<DropStackViewModel>? StackOpened;
    internal event EventHandler? NewStackRequested;
    internal event EventHandler? ClipboardStackRequested;
    internal Guid? SelectedStackId => (this.StackGrid.SelectedItem as DropStackViewModel)?.Model.Id;
    internal void ClearInsertionAdorner() => this._stackInsertionAdorner.Clear();

    internal void SelectStack(DropStackViewModel stack)
    {
        if (!this.ViewModel.VisibleStacks.Contains(stack)) { return; }
        this.StackGrid.SelectedItems.Clear();
        this.StackGrid.SelectedItem = stack;
        this.StackGrid.ScrollIntoView(stack);
    }

    internal void RefreshVisibleStacks()
    {
        var selectedIds = this.ViewModel.SelectedStacks.Select(stack => stack.Model.Id).ToHashSet();
        this._isRefreshing = true;
        try
        {
            this.ViewModel.Refresh();
            var selected = this.ViewModel.VisibleStacks.Where(stack => selectedIds.Contains(stack.Model.Id)).ToArray();
            if (!this.StackGrid.SelectedItems.OfType<DropStackViewModel>().ToHashSet().SetEquals(selected))
            {
                this.StackGrid.SelectedItems.Clear();
                foreach (var stack in selected) { this.StackGrid.SelectedItems.Add(stack); }
            }
        }
        finally { this._isRefreshing = false; }
        this.UpdateOverviewSelection();
    }

    private void OnStackFilterTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        this.ViewModel.FilterText = sender.Text;
        this.RefreshVisibleStacks();
    }

    private void OnStackGridSelectionChanged(object sender, SelectionChangedEventArgs args) => this.UpdateOverviewSelection();

    private void UpdateOverviewSelection()
    {
        if (!this._isRefreshing)
        {
            this.ViewModel.SetSelection(this.StackGrid.SelectedItems.OfType<DropStackViewModel>().ToArray());
        }
    }

    private void OpenStack(DropStackViewModel stack) => this.StackOpened?.Invoke(this, stack);
    private void OnNewStackClick(object sender, RoutedEventArgs args) => this.NewStackRequested?.Invoke(this, EventArgs.Empty);
    private void OnNewStackFromClipboardClick(object sender, RoutedEventArgs args) => this.ClipboardStackRequested?.Invoke(this, EventArgs.Empty);
    private void OnNewNoteClick(object sender, RoutedEventArgs args) => App.Current.CreateQuickNote(this.SelectedStackId);
    private void OnBrowseNotesClick(object sender, RoutedEventArgs args) => App.Current.ShowNotes();

    private void OnManualSortClick(object sender, RoutedEventArgs args) =>
        this.ApplyOverviewSort(StackOrganizerSortMode.Manual);

    private void OnNameSortClick(object sender, RoutedEventArgs args) =>
        this.ApplyOverviewSort(StackOrganizerSortMode.Name);

    private void OnItemCountSortClick(object sender, RoutedEventArgs args) =>
        this.ApplyOverviewSort(StackOrganizerSortMode.ItemCount);

    private void ApplyOverviewSort(StackOrganizerSortMode sortMode)
    {
        this.ViewModel.SortMode = sortMode;
        this.ManualSortItem.IsChecked = sortMode == StackOrganizerSortMode.Manual;
        this.NameSortItem.IsChecked = sortMode == StackOrganizerSortMode.Name;
        this.ItemCountSortItem.IsChecked = sortMode == StackOrganizerSortMode.ItemCount;
        ToolTipService.SetToolTip(this.SortButton, sortMode switch
        {
            StackOrganizerSortMode.Name => "Sort: Name",
            StackOrganizerSortMode.ItemCount => "Sort: Item count",
            _ => "Sort: Manual order"
        });
        this.RefreshVisibleStacks();
    }

    private void OnCompactLayoutClick(object sender, RoutedEventArgs args) =>
        this.ApplyOverviewLayout(StackOrganizerLayoutMode.Compact);

    private void OnMediumLayoutClick(object sender, RoutedEventArgs args) =>
        this.ApplyOverviewLayout(StackOrganizerLayoutMode.Medium);

    private void OnLargeLayoutClick(object sender, RoutedEventArgs args) =>
        this.ApplyOverviewLayout(StackOrganizerLayoutMode.Large);

    private void ApplyOverviewLayout(StackOrganizerLayoutMode layoutMode)
    {
        this.ViewModel.LayoutMode = layoutMode;
        this.CompactLayoutItem.IsChecked = layoutMode == StackOrganizerLayoutMode.Compact;
        this.MediumLayoutItem.IsChecked = layoutMode == StackOrganizerLayoutMode.Medium;
        this.LargeLayoutItem.IsChecked = layoutMode == StackOrganizerLayoutMode.Large;
        ToolTipService.SetToolTip(this.LayoutButton, layoutMode switch
        {
            StackOrganizerLayoutMode.Compact => "Layout: Compact",
            StackOrganizerLayoutMode.Large => "Layout: Large",
            _ => "Layout: Medium"
        });
        var styleKey = layoutMode switch
        {
            StackOrganizerLayoutMode.Compact => "OrganizerCompactStackItemStyle",
            StackOrganizerLayoutMode.Large => "OrganizerLargeStackItemStyle",
            _ => "OrganizerMediumStackItemStyle"
        };
        this.StackGrid.ItemContainerStyle = (Style)this.Resources[styleKey];
    }

    private DropStackViewModel? GetSingleSelectedOverviewStack() =>
        this.StackGrid.SelectedItems.Count == 1
            ? this.StackGrid.SelectedItems[0] as DropStackViewModel
            : null;

    private void OnOpenSelectedStackClick(object sender, RoutedEventArgs args)
    {
        if (this.GetSingleSelectedOverviewStack() is { } stack)
        {
            this.OpenStack(stack);
        }
    }

    private void OnStackGridDoubleTapped(object sender, DoubleTappedRoutedEventArgs args)
    {
        var element = args.OriginalSource as DependencyObject;
        while (element is not null)
        {
            if (element is FrameworkElement { DataContext: DropStackViewModel stack })
            {
                args.Handled = true;
                this.OpenStack(stack);
                return;
            }

            element = VisualTreeHelper.GetParent(element);
        }
    }

    private void OnStackDragStarting(UIElement sender, DragStartingEventArgs args)
    {
        var stack = GetTaggedStack(sender);
        if (stack is null)
        {
            args.Cancel = true;
            return;
        }

        DragDropDataService.Write(
            args.Data,
            stack.Model,
            stack.Name,
            App.Current.AllowMoveOnDragOutPreference);
    }

    private void OnStackDragSurfaceLoaded(object sender, RoutedEventArgs args)
    {
        if (sender is UIElement source)
        {
            source.AddHandler(
                UIElement.PointerMovedEvent,
                this._stackPointerMovedHandler,
                true);
        }
    }

    private void OnStackDragSurfaceUnloaded(object sender, RoutedEventArgs args)
    {
        if (sender is UIElement source)
        {
            source.RemoveHandler(UIElement.PointerMovedEvent, this._stackPointerMovedHandler);
        }
    }

    private async void OnStackPointerMoved(object sender, PointerRoutedEventArgs args)
    {
        if (this._isStackDragOperationActive ||
            !args.Pointer.IsInContact ||
            sender is not UIElement source ||
            GetTaggedStack(sender) is not { } stack)
        {
            return;
        }

        var pointerPoint = args.GetCurrentPoint(source);
        if (args.Pointer.PointerDeviceType == PointerDeviceType.Mouse &&
            !pointerPoint.Properties.IsLeftButtonPressed)
        {
            return;
        }

        this._isStackDragOperationActive = true;
        args.Handled = true;
        var dropResult = DataPackageOperation.None;
        try
        {
            dropResult = await source.StartDragAsync(pointerPoint);
        }
        catch (Exception exception)
        {
            App.Current.ShowToast(
                $"Could not start dragging {stack.Name}: {exception.Message}",
                InfoBarSeverity.Error);
        }
        finally
        {
            this._stackInsertionAdorner.Clear();
            await App.Current.CompleteStackDragAsync(dropResult);
            this._isStackDragOperationActive = false;
        }
    }

    private void OnStackGridDragOver(object sender, DragEventArgs args)
    {
        if (!DragDropDataService.HasStackReference(args.DataView))
        {
            return;
        }

        args.Handled = true;
        var canReorder = this.ViewModel.CanReorder;
        var target = canReorder
            ? this._stackInsertionAdorner.Resolve(args.GetPosition(this.StackGrid))
            : null;
        var source = DragDropDataService.ActiveStackReferenceId is { } stackId
            ? this._catalog.Stacks.FirstOrDefault(stack => stack.Model.Id == stackId)
            : null;
        var canMove = target is not null && (source is null ||
            (this.ViewModel.ScopeSide is { } side
                ? this._catalog.CanMoveStackToEdge(source, side, target.Value.InsertionIndex)
                : this._catalog.CanMoveStack(source, target.Value.InsertionIndex)));
        if (!canMove)
        {
            this._stackInsertionAdorner.Clear();
            args.AcceptedOperation = DataPackageOperation.None;
            args.DragUIOverride.Caption = !string.IsNullOrWhiteSpace(this.StackFilterBox.Text)
                ? "Clear the filter to reorder stacks"
                : this.ViewModel.SortMode != StackOrganizerSortMode.Manual
                    ? "Choose Manual order to reorder stacks"
                    : "Stack is already in this position";
        }
        else
        {
            this._stackInsertionAdorner.Show(target!.Value);
            args.AcceptedOperation = DragDropDataService.GetAcceptedInternalMoveOperation(args.DataView);
            args.DragUIOverride.Caption = this.ViewModel.ScopeSide is { } scopeSide
                ? $"Move stack here on the {scopeSide.GetDisplayName().ToLowerInvariant()} edge"
                : "Move stack here";
        }

        args.DragUIOverride.IsCaptionVisible = true;
        args.DragUIOverride.IsContentVisible = true;
    }

    private void OnStackGridDragLeave(object sender, DragEventArgs args)
    {
        if (DragDropDataService.HasStackReference(args.DataView))
        {
            this._stackInsertionAdorner.Clear();
        }
    }

    private async void OnStackGridDrop(object sender, DragEventArgs args)
    {
        if (!DragDropDataService.HasStackReference(args.DataView))
        {
            return;
        }

        args.Handled = true;
        var target = this.ViewModel.CanReorder
            ? this._stackInsertionAdorner.Resolve(args.GetPosition(this.StackGrid))
            : null;
        this._stackInsertionAdorner.Clear();
        if (target is null)
        {
            return;
        }

        var stackId = await DragDropDataService.ReadStackReferenceAsync(args.DataView);
        var stack = stackId is { } id
            ? this._catalog.Stacks.FirstOrDefault(candidate => candidate.Model.Id == id)
            : null;
        if (stack is null)
        {
            App.Current.ShowToast("That stack is no longer available.", InfoBarSeverity.Warning);
            return;
        }

        var moved = this.ViewModel.ScopeSide is { } side
            ? this._catalog.MoveStackToEdge(stack, side, target.Value.InsertionIndex)
            : this._catalog.MoveStack(stack, target.Value.InsertionIndex);
        if (moved)
        {
            this.RefreshVisibleStacks();
        }
    }

    private static DropStackViewModel? GetTaggedStack(object sender) =>
        (sender as FrameworkElement)?.Tag as DropStackViewModel;

    private void OnOpenTaggedStackClick(object sender, RoutedEventArgs args)
    {
        if ((sender as FrameworkElement)?.Tag is DropStackViewModel stack)
        {
            this.OpenStack(stack);
        }
    }

    private void OnOpenStackInTrayClick(object sender, RoutedEventArgs args)
    {
        if ((sender as FrameworkElement)?.Tag is DropStackViewModel stack)
        {
            App.Current.OpenTray(stack);
        }
    }

    private async void OnRenameTaggedStackClick(object sender, RoutedEventArgs args)
    {
        if ((sender as FrameworkElement)?.Tag is DropStackViewModel stack)
        {
            await StackDialogService.RenameAsync(this.RootGrid.XamlRoot, stack);
        }
    }

    private async void OnDeleteTaggedStackClick(object sender, RoutedEventArgs args)
    {
        if ((sender as FrameworkElement)?.Tag is DropStackViewModel stack)
        {
            await this.DeleteStackAsync(stack);
        }
    }

    private async Task DeleteStackAsync(DropStackViewModel stack)
    {
        if (!await StackDialogService.ConfirmDeleteAsync(this.RootGrid.XamlRoot, stack))
        {
            return;
        }

        await App.Current.DeleteStackAsync(stack);
        App.Current.ShowToast($"Deleted {stack.Name}.", InfoBarSeverity.Success);
    }

    private void OnOpenCurrentEdgeShelfClick(object sender, RoutedEventArgs args)
    {
        if (this.ViewModel.ScopeSide is { } side)
        {
            App.Current.ShowEdgeShelf(side);
        }
    }

    private void OnMoveSelectedToLeftEdgeClick(object sender, RoutedEventArgs args) =>
        this.AssignSelectedOverviewStacksToEdge(EdgeShelfSide.Left);

    private void OnMoveSelectedToRightEdgeClick(object sender, RoutedEventArgs args) =>
        this.AssignSelectedOverviewStacksToEdge(EdgeShelfSide.Right);

    private void OnMoveSelectedToTopEdgeClick(object sender, RoutedEventArgs args) =>
        this.AssignSelectedOverviewStacksToEdge(EdgeShelfSide.Top);

    private void OnMoveSelectedToBottomEdgeClick(object sender, RoutedEventArgs args) =>
        this.AssignSelectedOverviewStacksToEdge(EdgeShelfSide.Bottom);

    private void AssignSelectedOverviewStacksToEdge(EdgeShelfSide side)
    {
        var count = this.ViewModel.AssignSelectionToEdge(side);
        if (count > 0)
        {
            App.Current.ShowToast($"Moved {count} {(count == 1 ? "stack" : "stacks")} to the {side.GetDisplayName().ToLowerInvariant()} edge.", InfoBarSeverity.Success);
        }
    }

    private void OnRemoveSelectedFromEdgesClick(object sender, RoutedEventArgs args)
    {
        var count = this.ViewModel.AssignSelectionToEdge(null);
        if (count > 0)
        {
            App.Current.ShowToast($"Removed {count} {(count == 1 ? "stack" : "stacks")} from the edge shelves.", InfoBarSeverity.Success);
        }
    }
}
