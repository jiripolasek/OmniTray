// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Numerics;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using OmniTray.Controls;
using OmniTray.ViewModels.Organizer;

namespace OmniTray.Views.Organizer;

public sealed partial class StackOverviewPage : Page
{
    private const float StackHoverElevation = 16;
    private const double StackCombineDropZoneInsetRatio = 0.22;

    internal event EventHandler<DropStackViewModel>? StackOpened;
    internal event EventHandler? NewStackRequested;
    internal event EventHandler? ClipboardStackRequested;
    internal event EventHandler? DetailsPaneToggleRequested;
    private readonly MainViewModel _catalog;
    private readonly ListInsertionAdornerController _stackInsertionAdorner;
    private readonly DataTemplate _stackCardTemplate;
    private readonly ItemsPanelTemplate _stackCardItemsPanel;
    private readonly PointerEventHandler _stackPointerMovedHandler;
    private FrameworkElement? _stackCombineTargetRow;
    private FrameworkElement? _hoveredStackRow;
    private bool _isRefreshing;
    private bool _isStackDragOperationActive;

    public StackOverviewViewModel ViewModel { get; }
    internal Guid? SelectedStackId => (this.StackGrid.SelectedItem as DropStackViewModel)?.Model.Id;

    internal StackOverviewPage(MainViewModel catalog, StackOverviewViewModel viewModel)
    {
        this._catalog = catalog;
        this.ViewModel = viewModel;
        this._stackPointerMovedHandler = this.OnStackPointerMoved;
        this.InitializeComponent();
        OrganizerKeyboardAccelerators.ScopeTo(
            this.StackGrid,
            this.RenameSelectedStackButton,
            this.DeleteSelectedStacksButton);
        this._stackCardTemplate = this.StackGrid.ItemTemplate;
        this._stackCardItemsPanel = this.StackGrid.ItemsPanel;
        this._stackInsertionAdorner
            = new ListInsertionAdornerController(this.StackGrid, "StackInsertionAdorner", Orientation.Horizontal);
        this._stackInsertionAdorner.SetLayout(Orientation.Horizontal, true);
        this.ApplyOverviewLayout(this.ViewModel.LayoutMode);
    }

    internal void ClearInsertionAdorner() => this._stackInsertionAdorner.Clear();

    internal void SetDetailsPaneState(bool isVisible, bool isAvailable)
        => this.CommandToolbar.SetDetailsPaneState(isVisible, isAvailable);

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

    private void OnStackGridSelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        this.UpdateOverviewSelection();
        this.UpdateStackSelectionIndicators();
    }

    private void UpdateOverviewSelection()
    {
        if (!this._isRefreshing)
        {
            this.ViewModel.SetSelection(this.StackGrid.SelectedItems.OfType<DropStackViewModel>().ToArray());
        }

        var hasSelection = this.StackGrid.SelectedItems.Count > 0;
        this.CommandToolbar.IsSelectionActive = hasSelection;
        this.OpenSelectedStackButton.Visibility = this.ViewModel.CanOpenSelection
            ? Visibility.Visible
            : Visibility.Collapsed;
        this.RenameSelectedStackButton.Visibility = this.ViewModel.CanOpenSelection
            ? Visibility.Visible
            : Visibility.Collapsed;
        this.MoveSelectedStacksButton.Visibility = hasSelection ? Visibility.Visible : Visibility.Collapsed;
        this.DeleteSelectedStacksButton.Visibility = hasSelection ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnStackRowPointerEntered(object sender, PointerRoutedEventArgs args)
    {
        if (args.Pointer.PointerDeviceType != PointerDeviceType.Mouse ||
            sender is not FrameworkElement { Tag: DropStackViewModel } row)
        {
            return;
        }

        if (this._hoveredStackRow is { } previousRow && !ReferenceEquals(previousRow, row))
        {
            this.UpdateStackHoverShadow(previousRow, false);
            this.UpdateStackSelectionIndicator(previousRow, false);
        }

        this._hoveredStackRow = row;
        this.UpdateStackHoverShadow(row, true);
        this.UpdateStackSelectionIndicator(row, true);
    }

    private void OnStackRowPointerExited(object sender, PointerRoutedEventArgs args)
    {
        if (!ReferenceEquals(this._hoveredStackRow, sender))
        {
            return;
        }

        this._hoveredStackRow = null;
        if (sender is FrameworkElement row)
        {
            this.UpdateStackHoverShadow(row, false);
            this.UpdateStackSelectionIndicator(row, false);
        }
    }

    private void OnStackSelectionCheckBoxLoaded(object sender, RoutedEventArgs args)
    {
        if (sender is CheckBox checkBox)
        {
            this.UpdateStackSelectionCheckBox(
                checkBox,
                ReferenceEquals(this.FindStackRow(checkBox), this._hoveredStackRow));
        }
    }

    private void OnStackSelectionCheckBoxPointerPressed(object sender, PointerRoutedEventArgs args) =>
        args.Handled = true;

    private void OnStackSelectionCheckBoxClick(object sender, RoutedEventArgs args)
    {
        if (sender is not CheckBox { Tag: DropStackViewModel stack } checkBox ||
            !this.ViewModel.VisibleStacks.Contains(stack))
        {
            return;
        }

        if (checkBox.IsChecked == true)
        {
            if (!this.StackGrid.SelectedItems.Contains(stack))
            {
                this.StackGrid.SelectedItems.Add(stack);
            }
        }
        else
        {
            this.StackGrid.SelectedItems.Remove(stack);
        }

        this.UpdateStackSelectionIndicators();
    }

    private void UpdateStackSelectionIndicators()
    {
        foreach (var checkBox in FindDescendants<CheckBox>(this.StackGrid)
                     .Where(static candidate => candidate.Name == "StackSelectionCheckBox"))
        {
            var row = this.FindStackRow(checkBox);
            this.UpdateStackSelectionCheckBox(checkBox, ReferenceEquals(row, this._hoveredStackRow));
        }
    }

    private void UpdateStackSelectionIndicator(FrameworkElement row, bool isHovered)
    {
        var checkBox = FindDescendants<CheckBox>(row)
            .FirstOrDefault(static candidate => candidate.Name == "StackSelectionCheckBox");
        if (checkBox is not null)
        {
            this.UpdateStackSelectionCheckBox(checkBox, isHovered);
        }
    }

    private void UpdateStackSelectionCheckBox(CheckBox checkBox, bool isHovered)
    {
        if (checkBox.Tag is not DropStackViewModel stack)
        {
            return;
        }

        var isSelected = this.StackGrid.SelectedItems.Contains(stack);
        checkBox.IsChecked = isSelected;
        checkBox.Visibility = this.StackGrid.SelectedItems.Count > 0 || isHovered
            ? Visibility.Visible
            : Visibility.Collapsed;

        var row = this.FindStackRow(checkBox);
        var border = row is null
            ? null
            : FindDescendants<Border>(row)
                .FirstOrDefault(static candidate => candidate.Name == "StackSelectionBorder");
        if (border is not null)
        {
            border.Visibility = isSelected ? Visibility.Visible : Visibility.Collapsed;
        }

        var surface = row is null
            ? null
            : FindDescendants<Border>(row)
                .FirstOrDefault(static candidate => candidate.Name == "StackCardSurface");
        if (surface is not null)
        {
            surface.Opacity = isSelected || isHovered ? 1 : 0;
        }
    }

    private FrameworkElement? FindStackRow(DependencyObject child)
    {
        var current = VisualTreeHelper.GetParent(child);
        while (current is not null && current != this.StackGrid)
        {
            if (current is FrameworkElement { Tag: DropStackViewModel })
            {
                return (FrameworkElement)current;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private void UpdateStackHoverShadow(DependencyObject row, bool isHovered)
    {
        var container = this.FindStackContainer(row);
        var surface = FindDescendants<Border>(row)
            .FirstOrDefault(static candidate => candidate.Name == "StackCardSurface");
        if (container is null || surface is null)
        {
            return;
        }

        var translation = surface.Translation;
        surface.Translation = new Vector3(
            translation.X,
            translation.Y,
            isHovered ? StackHoverElevation : 0);
        surface.Shadow = isHovered ? new ThemeShadow() : null;
        Canvas.SetZIndex(container, isHovered ? 1 : 0);
    }

    private GridViewItem? FindStackContainer(DependencyObject child)
    {
        for (var current = child;
             current is not null && current != this.StackGrid;
             current = VisualTreeHelper.GetParent(current))
        {
            if (current is GridViewItem container)
            {
                return container;
            }
        }

        return null;
    }

    private static IEnumerable<T> FindDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childCount; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in FindDescendants<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private void OnStackGridKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key == VirtualKey.Escape && this.StackGrid.SelectedItems.Count > 0)
        {
            args.Handled = true;
            this.StackGrid.SelectedItems.Clear();
            return;
        }

        if (args.Key != VirtualKey.Space || this.GetFocusedStack() is not { } focusedStack)
        {
            return;
        }

        args.Handled = true;
        if (this.StackGrid.SelectedItems.Contains(focusedStack))
        {
            this.StackGrid.SelectedItems.Remove(focusedStack);
        }
        else
        {
            this.StackGrid.SelectedItems.Add(focusedStack);
        }

        this.UpdateStackSelectionIndicators();
    }

    private DropStackViewModel? GetFocusedStack()
    {
        for (var element = FocusManager.GetFocusedElement(this.XamlRoot) as DependencyObject;
             element is not null && !ReferenceEquals(element, this.StackGrid);
             element = VisualTreeHelper.GetParent(element))
        {
            if (element is GridViewItem container)
            {
                return this.StackGrid.ItemFromContainer(container) as DropStackViewModel;
            }
        }

        return this.StackGrid.SelectedItem as DropStackViewModel;
    }

    private void OnClearOverviewSelectionClick(object? sender, EventArgs args) =>
        this.StackGrid.SelectedItems.Clear();

    private void OnDetailsPaneToggleClick(object? sender, EventArgs args) =>
        this.DetailsPaneToggleRequested?.Invoke(this, EventArgs.Empty);

    private void OpenStack(DropStackViewModel stack) => this.StackOpened?.Invoke(this, stack);

    private void OnNewStackClick(object sender, RoutedEventArgs args) =>
        this.NewStackRequested?.Invoke(this, EventArgs.Empty);

    private void OnNewStackFromClipboardClick(object sender, RoutedEventArgs args) =>
        this.ClipboardStackRequested?.Invoke(this, EventArgs.Empty);

    private void OnNewNoteClick(object sender, RoutedEventArgs args) =>
        App.Current.CreateQuickNote(this.SelectedStackId);

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

    private void OnCollectionViewModeChanged(object? sender, EventArgs args) =>
        this.ApplyOverviewLayout(this.CommandToolbar.CollectionViewMode);

    private void ApplyOverviewLayout(OrganizerCollectionViewMode layoutMode)
    {
        this.ViewModel.LayoutMode = layoutMode;
        this.CommandToolbar.CollectionViewMode = layoutMode;
        var isList = layoutMode == OrganizerCollectionViewMode.List;
        this.StackGrid.ItemTemplate = isList
            ? (DataTemplate)this.Resources["OrganizerStackListTemplate"]
            : this._stackCardTemplate;
        this.StackGrid.ItemsPanel = isList
            ? (ItemsPanelTemplate)this.Resources["OrganizerStackListItemsPanel"]
            : this._stackCardItemsPanel;
        var styleKey = layoutMode switch
        {
            OrganizerCollectionViewMode.List => "OrganizerListStackItemStyle",
            OrganizerCollectionViewMode.Small => "OrganizerCompactStackItemStyle",
            OrganizerCollectionViewMode.Large => "OrganizerLargeStackItemStyle",
            _ => "OrganizerMediumStackItemStyle"
        };
        this.StackGrid.ItemContainerStyle = (Style)this.Resources[styleKey];
        this._stackInsertionAdorner.SetLayout(
            isList ? Orientation.Vertical : Orientation.Horizontal,
            !isList);
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
            this.UpdateStackHoverShadow(source, false);
            source.AddHandler(
                PointerMovedEvent,
                this._stackPointerMovedHandler,
                true);
        }
    }

    private void OnStackDragSurfaceUnloaded(object sender, RoutedEventArgs args)
    {
        if (sender is UIElement source)
        {
            source.RemoveHandler(PointerMovedEvent, this._stackPointerMovedHandler);
            this.UpdateStackHoverShadow(source, false);
            if (ReferenceEquals(this._hoveredStackRow, source))
            {
                this._hoveredStackRow = null;
            }

            if (ReferenceEquals(this._stackCombineTargetRow, source))
            {
                this.SetStackCombineTarget(null);
            }
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
            this.SetStackCombineTarget(null);
            await App.Current.CompleteStackDragAsync(dropResult);
            this._isStackDragOperationActive = false;
        }
    }

    private async void OnRenameSelectedStackClick(object sender, RoutedEventArgs args)
    {
        if (this.GetSingleSelectedOverviewStack() is { } stack)
        {
            await StackDialogService.RenameAsync(this.RootGrid.XamlRoot, stack);
        }
    }

    private async void OnDeleteSelectedStacksClick(object sender, RoutedEventArgs args) =>
        await this.DeleteStacksAsync(this.ViewModel.SelectedStacks);

    private void OnStackCombineDragOver(object sender, DragEventArgs args)
    {
        if (sender is not FrameworkElement { Tag: DropStackViewModel target } row ||
            !DragDropDataService.HasStackReference(args.DataView) ||
            !this.IsStackCombineDropZone(row, args))
        {
            this.SetStackCombineTarget(null);
            return;
        }

        args.Handled = true;
        this._stackInsertionAdorner.Clear();
        var source = DragDropDataService.ActiveStackReferenceId is { } sourceId
            ? this._catalog.Stacks.FirstOrDefault(stack => stack.Model.Id == sourceId)
            : null;
        var canCombine = this._catalog.Stacks.Contains(target) &&
                         (source is null || !ReferenceEquals(source, target));
        this.SetStackCombineTarget(canCombine ? row : null);
        args.AcceptedOperation = canCombine
            ? DragDropDataService.GetAcceptedInternalMoveOperation(args.DataView)
            : DataPackageOperation.None;
        args.DragUIOverride.Caption = canCombine
            ? source is null
                ? $"Combine stack into {target.Name}"
                : $"Combine {source.Name} into {target.Name}"
            : "A stack cannot be combined with itself";
        args.DragUIOverride.IsCaptionVisible = true;
        args.DragUIOverride.IsContentVisible = true;
    }

    private void OnStackCombineDragLeave(object sender, DragEventArgs args)
    {
        if (DragDropDataService.HasStackReference(args.DataView))
        {
            this.SetStackCombineTarget(null);
        }
    }

    private async void OnStackCombineDrop(object sender, DragEventArgs args)
    {
        if (sender is not FrameworkElement { Tag: DropStackViewModel target } row ||
            !DragDropDataService.HasStackReference(args.DataView) ||
            !this.IsStackCombineDropZone(row, args))
        {
            return;
        }

        args.Handled = true;
        this._stackInsertionAdorner.Clear();
        this.SetStackCombineTarget(null);
        var acceptedOperation = DragDropDataService.GetAcceptedInternalMoveOperation(args.DataView);
        var sourceId = await DragDropDataService.ReadStackReferenceAsync(args.DataView);
        var source = sourceId is { } id
            ? this._catalog.Stacks.FirstOrDefault(stack => stack.Model.Id == id)
            : null;
        if (source is null || ReferenceEquals(source, target) ||
            !this._catalog.CombineStacks(target, source))
        {
            args.AcceptedOperation = DataPackageOperation.None;
            return;
        }

        args.AcceptedOperation = acceptedOperation;
        this.RefreshVisibleStacks();
        App.Current.ShowToast(
            $"Combined {source.Name} into {target.Name}.",
            InfoBarSeverity.Success);
    }

    private bool IsStackCombineDropZone(FrameworkElement row, DragEventArgs args)
    {
        var position = args.GetPosition(row);
        if (this.ViewModel.LayoutMode == OrganizerCollectionViewMode.List)
        {
            var inset = row.ActualHeight * StackCombineDropZoneInsetRatio;
            return position.Y >= inset && position.Y <= row.ActualHeight - inset;
        }

        var horizontalInset = row.ActualWidth * StackCombineDropZoneInsetRatio;
        return position.X >= horizontalInset && position.X <= row.ActualWidth - horizontalInset;
    }

    private void SetStackCombineTarget(FrameworkElement? row)
    {
        if (ReferenceEquals(this._stackCombineTargetRow, row))
        {
            return;
        }

        SetStackCombineBorderVisible(this._stackCombineTargetRow, false);
        this._stackCombineTargetRow = row;
        SetStackCombineBorderVisible(row, true);
    }

    private static void SetStackCombineBorderVisible(DependencyObject? row, bool isVisible)
    {
        if (row is null)
        {
            return;
        }

        var border = FindDescendants<Border>(row)
            .FirstOrDefault(static candidate => candidate.Name == "StackCombineBorder");
        if (border is not null)
        {
            border.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void OnStackGridDragOver(object sender, DragEventArgs args)
    {
        if (!DragDropDataService.HasStackReference(args.DataView))
        {
            return;
        }

        args.Handled = true;
        this.SetStackCombineTarget(null);
        var canReorder = this.ViewModel.CanReorder;
        var target = canReorder
            ? this._stackInsertionAdorner.Resolve(args.GetPosition(this.StackGrid))
            : null;
        var source = DragDropDataService.ActiveStackReferenceId is { } stackId
            ? this._catalog.Stacks.FirstOrDefault(stack => stack.Model.Id == stackId)
            : null;
        var canMove = target is not null && (source is null ||
                                             (this.ViewModel.ScopeSide is { } side
                                                 ? this._catalog.CanMoveStackToEdge(source, side,
                                                     target.Value.InsertionIndex)
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
            this.SetStackCombineTarget(null);
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
        this.SetStackCombineTarget(null);
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

    private Task DeleteStackAsync(DropStackViewModel stack) => this.DeleteStacksAsync([stack]);

    private async Task DeleteStacksAsync(IReadOnlyList<DropStackViewModel> stacks)
    {
        var selected = stacks
            .Distinct()
            .Where(this._catalog.Stacks.Contains)
            .ToArray();
        if (selected.Length == 0 ||
            !await StackDialogService.ConfirmDeleteAsync(this.RootGrid.XamlRoot, selected))
        {
            return;
        }

        var deletedNames = new List<string>();
        foreach (var stack in selected.Where(this._catalog.Stacks.Contains))
        {
            deletedNames.Add(stack.Name);
            await App.Current.DeleteStackAsync(stack);
        }

        if (deletedNames.Count > 0)
        {
            App.Current.ShowToast(
                deletedNames is [var name] ? $"Deleted {name}." : $"Deleted {deletedNames.Count} stacks.",
                InfoBarSeverity.Success);
        }
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
            App.Current.ShowToast(
                $"Moved {count} {(count == 1 ? "stack" : "stacks")} to the {side.GetDisplayName().ToLowerInvariant()} edge.",
                InfoBarSeverity.Success);
        }
    }

    private void OnRemoveSelectedFromEdgesClick(object sender, RoutedEventArgs args)
    {
        var count = this.ViewModel.AssignSelectionToEdge(null);
        if (count > 0)
        {
            App.Current.ShowToast($"Removed {count} {(count == 1 ? "stack" : "stacks")} from the edge shelves.",
                InfoBarSeverity.Success);
        }
    }
}
