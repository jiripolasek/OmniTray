// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using OmniTray.Controls;
using DispatcherQueuePriority = Microsoft.UI.Dispatching.DispatcherQueuePriority;

namespace OmniTray.Views;

public sealed partial class MainPage : Page
{
    private readonly ObservableCollection<DropStackViewModel> _filteredStacks = [];
    private readonly ListInsertionAdornerController _stackInsertionAdorner;
    private readonly PointerEventHandler _stackPointerMovedHandler;
    private readonly HashSet<DropStackViewModel> _trackedStacks = [];
    private FrameworkElement? _expandedStackOrganizer;
    private TrayInspectorPopupHost? _inspectorPopupHost;
    private bool _isDragOverPopup;
    private bool _isFilterApplied;
    private bool _isStackDragOperationActive;

    public MainPage()
    {
        this._stackPointerMovedHandler = this.OnStackPointerMoved;
        this.InitializeComponent();
        // AutoSuggestBox handles Escape inside its template. Listen for already-handled
        // key events at the page root so the popup behavior still gets a chance to run.
        this.AddHandler(
            KeyDownEvent,
            new KeyEventHandler(this.OnPageKeyDown),
            true);
        this._stackInsertionAdorner = new ListInsertionAdornerController(this.StackList,
            "StackInsertionAdorner",
            Orientation.Vertical);
        this.PopupSectionSelector.SelectionChanged += this.OnPopupSectionSelectionChanged;
        this.PopupCommandSurface.ContentAvailabilityChanged += this.OnPopupCommandContentAvailabilityChanged;
        this.ViewModel.Stacks.CollectionChanged += this.OnStacksChanged;
        this.SynchronizeTrackedStacks();
        this.UpdateEmptyState();
        this.UpdateCommandEmptyState();
        this.UpdateSelectedPopupSection();
    }

    internal void SetOwnerWindow(Window owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        this.PopupCommandSurface.OwnerWindow = owner;
        this._inspectorPopupHost ??= new TrayInspectorPopupHost(owner, this.StackInspectorPopup);
    }

    internal void CloseStackInspector() => this._inspectorPopupHost?.Close();

    internal void DisposeStackInspector()
    {
        this._inspectorPopupHost?.Dispose();
        this._inspectorPopupHost = null;
    }

    public MainViewModel ViewModel => App.Current.StackCatalogViewModel;

    private void OnDragOver(object sender, DragEventArgs args)
    {
        this._isDragOverPopup = true;

        if (this.IsCommandsTabSelected)
        {
            args.Handled = true;
            args.AcceptedOperation = DataPackageOperation.None;
            args.DragUIOverride.Caption = "Drop onto a command";
            args.DragUIOverride.IsCaptionVisible = true;
            this.DropHintOverlay.Visibility = Visibility.Collapsed;
            return;
        }

        if (DragDropDataService.HasItemReference(args.DataView))
        {
            args.Handled = true;
            args.AcceptedOperation = DataPackageOperation.None;
            args.DragUIOverride.Caption = "Drop onto a stack or New stack";
            args.DragUIOverride.IsCaptionVisible = true;
            this.DropHintOverlay.Visibility = Visibility.Collapsed;
            return;
        }

        if (DragDropDataService.HasStackReference(args.DataView))
        {
            this.DropHintOverlay.Visibility = Visibility.Collapsed;
            return;
        }

        if (!ConfigureDragOver(args, "Create a new stack"))
        {
            this.DropHintOverlay.Visibility = Visibility.Collapsed;
            return;
        }

        this.DropHintOverlay.Visibility = Visibility.Visible;
    }

    private void OnDragLeave(object sender, DragEventArgs args)
    {
        this._isDragOverPopup = false;
        this.DropHintOverlay.Visibility = Visibility.Collapsed;
    }

    private async void OnDrop(object sender, DragEventArgs args)
    {
        this._isDragOverPopup = false;
        this.DropHintOverlay.Visibility = Visibility.Collapsed;
        if (this.IsCommandsTabSelected)
        {
            args.Handled = true;
            args.AcceptedOperation = DataPackageOperation.None;
            return;
        }

        if (DragDropDataService.HasStackReference(args.DataView))
        {
            return;
        }

        if (DragDropDataService.HasItemReference(args.DataView))
        {
            args.Handled = true;
            return;
        }

        await this.CreateStackFromDropAsync(args.DataView);
    }

    private void OnStackDragOver(object sender, DragEventArgs args)
    {
        if (DragDropDataService.HasItemReference(args.DataView))
        {
            args.Handled = true;
            this.DropHintOverlay.Visibility = Visibility.Collapsed;
            var target = GetStack(sender);
            var isSameStack = target is not null &&
                              DragDropDataService.ActiveItemReference?.SourceStackId == target.Model.Id;
            SetStackDropOutline(sender, target is not null && !isSameStack);
            if (target is not null)
            {
                ConfigureItemTransferDragOver(args, $"Add to {target.Name}", target);
            }

            return;
        }

        if (DragDropDataService.HasStackReference(args.DataView))
        {
            SetStackDropOutline(sender, false);
            return;
        }

        args.Handled = true;
        this.DropHintOverlay.Visibility = Visibility.Collapsed;

        var stack = GetStack(sender);
        var canAccept = stack is not null && ConfigureDragOver(args, $"Add to {stack.Name}");
        SetStackDropOutline(sender, canAccept);
    }

    private void OnStackDragLeave(object sender, DragEventArgs args)
    {
        SetStackDropOutline(sender, false);
        args.Handled = !DragDropDataService.HasStackReference(args.DataView);
    }

    private async void OnStackDrop(object sender, DragEventArgs args)
    {
        if (DragDropDataService.HasStackReference(args.DataView))
        {
            SetStackDropOutline(sender, false);
            return;
        }

        args.Handled = true;
        this.DropHintOverlay.Visibility = Visibility.Collapsed;
        SetStackDropOutline(sender, false);

        var stack = GetStack(sender);
        if (stack is null)
        {
            return;
        }

        if (DragDropDataService.HasItemReference(args.DataView))
        {
            if (DragDropDataService.ActiveItemReference?.SourceStackId == stack.Model.Id)
            {
                return;
            }

            await this.TransferItemsIntoStackAsync(
                args.DataView,
                stack,
                stack.Items.Count,
                IsCopyRequested(args));
            return;
        }

        try
        {
            var items = await DragDropDataService.ReadAsync(args.DataView);
            if (items.Count == 0)
            {
                ShowStatus("This drag did not contain a supported payload.", InfoBarSeverity.Warning);
                return;
            }

            if (!this.ViewModel.Stacks.Contains(stack))
            {
                return;
            }

            var addedCount = stack.AppendDroppedItems(items);
            ShowDropImportStatus(stack.Name, items.Count, addedCount);
        }
        catch (Exception exception)
        {
            ShowStatus($"The drop could not be captured: {exception.Message}", InfoBarSeverity.Error);
        }
    }

    /* Extra drop target(?)
    private void OnNewStackDragOver(object sender, DragEventArgs args)
    {
        args.Handled = true;
        this.DropHintOverlay.Visibility = Visibility.Collapsed;
        if (DragDropDataService.HasStackReference(args.DataView))
        {
            args.AcceptedOperation = DataPackageOperation.None;
            this.NewStackDropOutline.Visibility = Visibility.Collapsed;
            return;
        }

        if (DragDropDataService.HasItemReference(args.DataView))
        {
            ConfigureItemTransferDragOver(args, "Create a new stack");
            this.NewStackDropOutline.Visibility = Visibility.Visible;
            return;
        }

        this.NewStackDropOutline.Visibility = ConfigureDragOver(args, "Create a new stack")
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void OnNewStackDragLeave(object sender, DragEventArgs args)
    {
        args.Handled = true;
        this.NewStackDropOutline.Visibility = Visibility.Collapsed;
    }

    private async void OnNewStackDrop(object sender, DragEventArgs args)
    {
        args.Handled = true;
        this.DropHintOverlay.Visibility = Visibility.Collapsed;
        this.NewStackDropOutline.Visibility = Visibility.Collapsed;
        if (DragDropDataService.HasStackReference(args.DataView))
        {
            return;
        }

        if (DragDropDataService.HasItemReference(args.DataView))
        {
            await this.CreateStackFromItemDropAsync(args.DataView, IsCopyRequested(args));
            return;
        }

        await this.CreateStackFromDropAsync(args.DataView);
    }
    */

    private void OnNewStackClick(object sender, RoutedEventArgs args)
    {
        this.ViewModel.AddStack(DropStack.CreateEmpty());
        ShowStatus("Created an empty stack.", InfoBarSeverity.Success);
    }

    private async void OnPasteAsNewStackClick(object sender, RoutedEventArgs args)
    {
        DataPackageView dataView;
        try
        {
            dataView = Clipboard.GetContent();
        }
        catch (Exception exception)
        {
            ShowStatus($"The clipboard could not be read: {exception.Message}", InfoBarSeverity.Error);
            return;
        }

        await this.CreateStackFromDataPackageAsync(
            dataView,
            "The clipboard does not contain files, folders, text, or an image.",
            "The clipboard content could not be captured",
            CaptureChannel.Clipboard);
    }

    private void OnStackDragStarting(UIElement sender, DragStartingEventArgs args)
    {
        var stack = GetStack(sender);
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
                PointerMovedEvent, this._stackPointerMovedHandler,
                true);
        }
    }

    private void OnStackDragSurfaceUnloaded(object sender, RoutedEventArgs args)
    {
        if (sender is UIElement source)
        {
            source.RemoveHandler(PointerMovedEvent, this._stackPointerMovedHandler);
        }
    }

    private async void OnStackPointerMoved(object sender, PointerRoutedEventArgs args)
    {
        if (this._isStackDragOperationActive ||
            !args.Pointer.IsInContact ||
            sender is not UIElement source ||
            GetStack(sender) is not { } stack)
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
            ShowStatus($"Could not start dragging {stack.Name}: {exception.Message}", InfoBarSeverity.Error);
        }
        finally
        {
            this._stackInsertionAdorner.Clear();
            await App.Current.CompleteStackDragAsync(dropResult);
            this._isStackDragOperationActive = false;
        }
    }

    private void OnStackListDragOver(object sender, DragEventArgs args)
    {
        if (!DragDropDataService.HasStackReference(args.DataView))
        {
            return;
        }

        args.Handled = true;
        this.DropHintOverlay.Visibility = Visibility.Collapsed;
        var target = this._stackInsertionAdorner.Resolve(args.GetPosition(this.StackList));
        var source = DragDropDataService.ActiveStackReferenceId is { } stackId
            ? this.ViewModel.Stacks.FirstOrDefault(stack => stack.Model.Id == stackId)
            : null;
        var canMove = !this._isFilterApplied &&
                      target is not null &&
                      (source is null || this.ViewModel.CanMoveStack(source, target.Value.InsertionIndex));
        if (!canMove)
        {
            this._stackInsertionAdorner.Clear();
            args.AcceptedOperation = DataPackageOperation.None;
            args.DragUIOverride.Caption = this._isFilterApplied
                ? "Clear the filter to reorder stacks"
                : "Stack is already in this position";
        }
        else
        {
            this._stackInsertionAdorner.Show(target!.Value);
            args.AcceptedOperation = DragDropDataService.GetAcceptedInternalMoveOperation(args.DataView);
            args.DragUIOverride.Caption = "Move stack here";
        }

        args.DragUIOverride.IsCaptionVisible = true;
        args.DragUIOverride.IsContentVisible = true;
    }

    private void OnStackListDragLeave(object sender, DragEventArgs args)
    {
        if (DragDropDataService.HasStackReference(args.DataView))
        {
            this._stackInsertionAdorner.Clear();
        }
    }

    private async void OnStackListDrop(object sender, DragEventArgs args)
    {
        if (!DragDropDataService.HasStackReference(args.DataView))
        {
            return;
        }

        args.Handled = true;
        var target = this._stackInsertionAdorner.Resolve(args.GetPosition(this.StackList));
        this._stackInsertionAdorner.Clear();
        if (this._isFilterApplied || target is null)
        {
            return;
        }

        var stackId = await DragDropDataService.ReadStackReferenceAsync(args.DataView);
        var stack = stackId is { } id
            ? this.ViewModel.Stacks.FirstOrDefault(candidate => candidate.Model.Id == id)
            : null;
        if (stack is null)
        {
            ShowStatus("That stack is no longer available.", InfoBarSeverity.Warning);
            return;
        }

        this.ViewModel.MoveStack(stack, target.Value.InsertionIndex);
    }

    private void OnStackHeaderClick(object sender, RoutedEventArgs args)
    {
        var root = FindStackRoot(sender as DependencyObject);
        if (root?.FindName("OrganizerPanel") is not FrameworkElement organizer)
        {
            return;
        }

        var expand = organizer.Visibility != Visibility.Visible;
        if (expand && this._expandedStackOrganizer is { } previous && !ReferenceEquals(previous, root))
        {
            SetStackExpansionVisual(previous, false);
        }

        SetStackExpansionVisual(root, expand);
        if (expand)
        {
            this._expandedStackOrganizer = root;
        }
        else if (ReferenceEquals(this._expandedStackOrganizer, root))
        {
            this._expandedStackOrganizer = null;
        }
    }

    private void OnStackHeaderPointerEntered(object sender, PointerRoutedEventArgs args) =>
        SetStackHeaderHover(sender as FrameworkElement, true);

    private void OnStackHeaderPointerExited(object sender, PointerRoutedEventArgs args) =>
        SetStackHeaderHover(sender as FrameworkElement, false);

    private static void SetStackHeaderHover(FrameworkElement? header, bool isPointerOver)
    {
        if (header?.FindName("StackHeaderHoverBackground") is Border hoverBackground)
        {
            hoverBackground.Opacity = isPointerOver ? 1 : 0;
        }

        SetStackHeaderActionHover(header, "InspectorButton", isPointerOver);
        SetStackHeaderActionHover(header, "PopOutButton", isPointerOver);
    }

    private static void SetStackHeaderActionHover(
        FrameworkElement? header,
        string buttonName,
        bool isPointerOver)
    {
        if (header?.FindName(buttonName) is Button button)
        {
            button.Opacity = isPointerOver ? 0.92 : 0;
            button.IsHitTestVisible = isPointerOver;
        }
    }

    private void OnInspectStackClick(object sender, RoutedEventArgs args)
    {
        if (this._inspectorPopupHost is not { } inspectorPopupHost ||
            GetTaggedStack(sender) is not { } stack)
        {
            return;
        }

        var placementTarget = sender as Button ??
                              this.StackList.ContainerFromItem(stack) as FrameworkElement ??
                              this.RootGrid;
        if (sender is MenuFlyoutItem)
        {
            this.DispatcherQueue.TryEnqueue(
                DispatcherQueuePriority.Low,
                () => inspectorPopupHost.Show(placementTarget, stack, TrayInspectorPlacement.Left));
            return;
        }

        inspectorPopupHost.Show(placementTarget, stack, TrayInspectorPlacement.Left);
    }

    private void OnOpenTrayMenuClick(object sender, RoutedEventArgs args)
    {
        if (GetTaggedStack(sender) is { } stack)
        {
            App.Current.OpenTray(stack);
        }
    }

    private void OnOpenStackOrganizerClick(object sender, RoutedEventArgs args) =>
        App.Current.ShowStackOrganizer(GetTaggedStack(sender));

    private static void SetStackExpansionVisual(FrameworkElement root, bool isExpanded)
    {
        if (root.FindName("ExpandedSurface") is Border surface)
        {
            surface.Visibility = isExpanded ? Visibility.Visible : Visibility.Collapsed;
        }

        if (root.FindName("OrganizerPanel") is FrameworkElement organizer)
        {
            organizer.Visibility = isExpanded ? Visibility.Visible : Visibility.Collapsed;
        }

        if (root.FindName("ExpansionGlyph") is FontIcon glyph)
        {
            glyph.Glyph = isExpanded ? "\uE70E" : "\uE70D";
        }
    }

    private static FrameworkElement? FindStackRoot(DependencyObject? element)
    {
        while (element is not null)
        {
            if (element is FrameworkElement { Name: "StackRoot" } root)
            {
                return root;
            }

            element = VisualTreeHelper.GetParent(element);
        }

        return null;
    }

    private void OnAddToLeftEdgeMenuClick(object sender, RoutedEventArgs args) =>
        this.AssignTaggedStackToEdge(sender, EdgeShelfSide.Left);

    private void OnAddToRightEdgeMenuClick(object sender, RoutedEventArgs args) =>
        this.AssignTaggedStackToEdge(sender, EdgeShelfSide.Right);

    private void OnAddToTopEdgeMenuClick(object sender, RoutedEventArgs args) =>
        this.AssignTaggedStackToEdge(sender, EdgeShelfSide.Top);

    private void OnAddToBottomEdgeMenuClick(object sender, RoutedEventArgs args) =>
        this.AssignTaggedStackToEdge(sender, EdgeShelfSide.Bottom);

    private void AssignTaggedStackToEdge(object sender, EdgeShelfSide side)
    {
        if (GetTaggedStack(sender) is { } stack && this.ViewModel.AssignStackToEdge(stack, side))
        {
            ShowStatus(
                $"Placed {stack.Name} on the {side.GetDisplayName().ToLowerInvariant()} edge.",
                InfoBarSeverity.Success);
        }
    }

    private void OnRemoveFromEdgeMenuClick(object sender, RoutedEventArgs args)
    {
        if (GetTaggedStack(sender) is { } stack && this.ViewModel.RemoveStackFromEdge(stack))
        {
            ShowStatus($"Hid {stack.Name} from the edge shelf.", InfoBarSeverity.Success);
        }
    }

    private async void OnRenameStackMenuClick(object sender, RoutedEventArgs args)
    {
        if (GetTaggedStack(sender) is { } stack)
        {
            await StackDialogService.RenameAsync(this.RootGrid.XamlRoot, stack);
        }
    }

    private async void OnInsertClipboardContentMenuClick(object sender, RoutedEventArgs args)
    {
        if (GetTaggedStack(sender) is { } stack)
        {
            await App.Current.InsertClipboardContentAsync(stack);
        }
    }

    private void OnColorTintMenuClick(object sender, RoutedEventArgs args)
    {
        if (GetTaggedStack(sender) is not { } stack)
        {
            return;
        }

        var target = this.StackList.ContainerFromItem(stack) as FrameworkElement ?? this.RootGrid;
        TrayColorPaletteFlyout.Show(target, () => stack.Tint, stack.ChangeTint);
    }

    private async void OnDeleteStackMenuClick(object sender, RoutedEventArgs args)
    {
        var stack = GetTaggedStack(sender);
        if (stack is null ||
            !await StackDialogService.ConfirmDeleteAsync(this.RootGrid.XamlRoot, stack))
        {
            return;
        }

        await App.Current.DeleteStackAsync(stack);
        ShowStatus($"Deleted {stack.Name}.", InfoBarSeverity.Success);
    }

    private async void OnClearAllClick(object sender, RoutedEventArgs args)
    {
        var stackCount = this.ViewModel.Stacks.Count;
        if (stackCount == 0 ||
            !await StackDialogService.ConfirmClearAsync(this.RootGrid.XamlRoot, stackCount))
        {
            return;
        }

        await App.Current.ClearStacksAsync();
        ShowStatus("Deleted all stacks.", InfoBarSeverity.Success);
    }

    private void OnSettingsClick(object sender, RoutedEventArgs args) => App.Current.ShowSettings();

    private void OnShowEdgeShelfClick(object sender, RoutedEventArgs args) => App.Current.ShowEdgeShelf();

    private void OnSearchClick(object sender, RoutedEventArgs args)
    {
        if (this.FilterBox.Visibility == Visibility.Visible)
        {
            this.HideFilter();
        }
        else
        {
            this.ShowFilter();
        }
    }

    private void OnSearchAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        this.ShowFilter();
        args.Handled = true;
    }

    private void OnFilterBoxTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args) =>
        this.ApplyFilter();

    private void OnPageKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key != VirtualKey.Escape)
        {
            return;
        }

        if (this.TryHandleEscape())
        {
            args.Handled = true;
        }
    }

    private bool TryHandleEscape()
    {
        if (this.FilterBox.Visibility == Visibility.Visible)
        {
            if (!string.IsNullOrEmpty(this.FilterBox.Text))
            {
                this.FilterBox.Text = string.Empty;
                this.FocusFilterBox();
            }
            else
            {
                this.HideFilter();
                this.SearchButton.Focus(FocusState.Programmatic);
            }

            return true;
        }

        if (DragDropDataService.HasActiveDrag || this._isDragOverPopup)
        {
            // Leave Escape to the native drag loop so it cancels only the drag.
            return false;
        }

        App.Current.HidePopup();
        return true;
    }

    private void ShowFilter()
    {
        this.PopupSectionSelector.SelectedIndex = 0;
        this.FilterBox.Visibility = Visibility.Visible;
        this.DispatcherQueue.TryEnqueue(
            DispatcherQueuePriority.Low, this.FocusFilterBox);
    }

    private void FocusFilterBox()
    {
        var focusTarget = (Control?)FindVisualChild<TextBox>(this.FilterBox) ?? this.FilterBox;
        focusTarget.Focus(FocusState.Programmatic);
    }

    private static T? FindVisualChild<T>(DependencyObject parent)
        where T : DependencyObject
    {
        var childCount = VisualTreeHelper.GetChildrenCount(parent);
        for (var index = 0; index < childCount; index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
            {
                return match;
            }

            if (FindVisualChild<T>(child) is { } descendant)
            {
                return descendant;
            }
        }

        return null;
    }

    private void HideFilter()
    {
        this.FilterBox.Text = string.Empty;
        this.FilterBox.Visibility = Visibility.Collapsed;
        this.ApplyFilter();
    }

    private void OnStacksChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        this.SynchronizeTrackedStacks();
        this.ApplyFilter();
    }

    private void OnStackPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (this._isFilterApplied && args.PropertyName == nameof(DropStackViewModel.Model))
        {
            this.ApplyFilter();
        }
    }

    private void SynchronizeTrackedStacks()
    {
        var currentStacks = this.ViewModel.Stacks.ToHashSet();
        foreach (var staleStack in this._trackedStacks.Where(stack => !currentStacks.Contains(stack)).ToArray())
        {
            staleStack.PropertyChanged -= this.OnStackPropertyChanged;
            this._trackedStacks.Remove(staleStack);
        }

        foreach (var stack in currentStacks.Where(stack => !this._trackedStacks.Contains(stack)))
        {
            stack.PropertyChanged += this.OnStackPropertyChanged;
            this._trackedStacks.Add(stack);
        }
    }

    private void ApplyFilter()
    {
        var query = this.FilterBox.Text.Trim();
        if (query.Length == 0)
        {
            if (this._isFilterApplied)
            {
                this._isFilterApplied = false;
                this._filteredStacks.Clear();
                this.StackList.ItemsSource = this.ViewModel.Stacks;
                this.StackList.CanReorderItems = false;
            }

            this.UpdateEmptyState();
            return;
        }

        if (!this._isFilterApplied)
        {
            this._isFilterApplied = true;
            this.StackList.ItemsSource = this._filteredStacks;
            this.StackList.CanReorderItems = false;
        }

        var matches = this.ViewModel.Stacks
            .Where(stack => StackFilter.Matches(stack.Model, query))
            .ToArray();
        this._filteredStacks.Clear();
        foreach (var stack in matches)
        {
            this._filteredStacks.Add(stack);
        }

        this.UpdateEmptyState();
    }

    private void UpdateEmptyState()
    {
        var hasNoMatches = this._isFilterApplied && this._filteredStacks.Count == 0 && !this.ViewModel.IsEmpty;
        this.EmptyState.Visibility = this.ViewModel.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
        this.NoFilterResultsState.Visibility = hasNoMatches ? Visibility.Visible : Visibility.Collapsed;
        this.StackList.Visibility = this.ViewModel.IsEmpty || hasNoMatches
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private bool IsCommandsTabSelected => this.PopupSectionSelector.SelectedIndex == 1;

    private void OnPopupSectionSelectionChanged(object sender, SelectionChangedEventArgs args) =>
        this.UpdateSelectedPopupSection();

    private void UpdateSelectedPopupSection()
    {
        var showCommands = this.IsCommandsTabSelected;
        this.StacksTabContent.Visibility = showCommands ? Visibility.Collapsed : Visibility.Visible;
        this.CommandsTabContent.Visibility = showCommands ? Visibility.Visible : Visibility.Collapsed;
        this.SearchButton.Visibility = showCommands ? Visibility.Collapsed : Visibility.Visible;
        this.DropHintOverlay.Visibility = Visibility.Collapsed;
        this._isDragOverPopup = false;

        if (showCommands)
        {
            this.HideFilter();
        }
    }

    private void OnPopupCommandContentAvailabilityChanged(object? sender, EventArgs args) =>
        this.UpdateCommandEmptyState();

    private void UpdateCommandEmptyState()
    {
        this.CommandsEmptyState.Visibility = this.PopupCommandSurface.HasContent
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private Task CreateStackFromDropAsync(DataPackageView dataView) =>
        this.CreateStackFromDataPackageAsync(
            dataView,
            "This drag did not contain a supported payload.",
            "The drop could not be captured");

    private async Task CreateStackFromDataPackageAsync(
        DataPackageView dataView,
        string emptyMessage,
        string failureMessage,
        CaptureChannel channel = CaptureChannel.Drag)
    {
        try
        {
            var items = await DragDropDataService.ReadAsync(dataView, channel);
            if (items.Count == 0)
            {
                ShowStatus(emptyMessage, InfoBarSeverity.Warning);
                return;
            }

            this.ViewModel.AddStack(DropStack.Create(items));
            ShowStatus(
                items.Count == 1 ? "Created a stack with 1 item." : $"Created a stack with {items.Count} items.",
                InfoBarSeverity.Success);
        }
        catch (Exception exception)
        {
            ShowStatus($"{failureMessage}: {exception.Message}", InfoBarSeverity.Error);
        }
    }

    private async Task CreateStackFromItemDropAsync(DataPackageView dataView, bool copy)
    {
        var itemReference = await DragDropDataService.ReadItemReferenceAsync(dataView);
        var source = itemReference is null
            ? null
            : this.ViewModel.Stacks.FirstOrDefault(stack => stack.Model.Id == itemReference.SourceStackId);
        if (itemReference is null || source is null)
        {
            ShowStatus("Those items are no longer available.", InfoBarSeverity.Warning);
            return;
        }

        var selectedIds = itemReference.ItemIds.ToHashSet();
        var selectedItems = source.Model.Items.Where(item => selectedIds.Contains(item.Id)).ToArray();
        if (selectedItems.Length != selectedIds.Count)
        {
            ShowStatus("Those items are no longer available.", InfoBarSeverity.Warning);
            return;
        }

        var name = selectedItems.Length == 1 ? selectedItems[0].DisplayName : $"{selectedItems.Length} items";
        var created = this.ViewModel.AddStack(DropStack.CreateEmpty(name, source.Tint));
        try
        {
            if (!await App.Current.TransferItemsAsync(itemReference, created, 0, copy))
            {
                this.ViewModel.RemoveStack(created);
                ShowStatus("The drop did not create a stack.", InfoBarSeverity.Warning);
                return;
            }

            ShowStatus(
                $"{(copy ? "Copied" : "Moved")} {selectedItems.Length} {(selectedItems.Length == 1 ? "item" : "items")} into a new stack.",
                InfoBarSeverity.Success);
        }
        catch (Exception exception)
        {
            this.ViewModel.RemoveStack(created);
            ShowStatus($"The stack could not be created: {exception.Message}", InfoBarSeverity.Error);
        }
    }

    private async Task TransferItemsIntoStackAsync(
        DataPackageView dataView,
        DropStackViewModel target,
        int targetIndex,
        bool copy)
    {
        var itemReference = await DragDropDataService.ReadItemReferenceAsync(dataView);
        if (itemReference is null)
        {
            ShowStatus("Those items are no longer available.", InfoBarSeverity.Warning);
            return;
        }

        copy = copy && itemReference.SourceStackId != target.Model.Id;

        try
        {
            if (!await App.Current.TransferItemsAsync(itemReference, target, targetIndex, copy))
            {
                ShowStatus("The drop did not change the stack.", InfoBarSeverity.Informational);
                return;
            }

            ShowStatus(
                $"{(copy ? "Copied" : "Moved")} {itemReference.ItemIds.Count} {(itemReference.ItemIds.Count == 1 ? "item" : "items")} to {target.Name}.",
                InfoBarSeverity.Success);
        }
        catch (Exception exception)
        {
            ShowStatus($"The items could not be organized: {exception.Message}", InfoBarSeverity.Error);
        }
    }

    private static bool ConfigureDragOver(DragEventArgs args, string caption)
    {
        if (!DragDropDataService.HasSupportedFormat(args.DataView))
        {
            args.AcceptedOperation = DataPackageOperation.None;
            return false;
        }

        args.AcceptedOperation = DataPackageOperation.Copy;
        args.DragUIOverride.Caption = caption;
        args.DragUIOverride.IsCaptionVisible = true;
        args.DragUIOverride.IsContentVisible = true;
        return true;
    }

    private static void ConfigureItemTransferDragOver(
        DragEventArgs args,
        string caption,
        DropStackViewModel? target = null)
    {
        var sameStack = target is not null &&
                        DragDropDataService.ActiveItemReference?.SourceStackId == target.Model.Id;
        if (sameStack)
        {
            args.AcceptedOperation = DataPackageOperation.None;
            args.DragUIOverride.Caption = "Item is already in this stack";
            args.DragUIOverride.IsCaptionVisible = true;
            args.DragUIOverride.IsContentVisible = true;
            return;
        }

        var copy = !sameStack && IsCopyRequested(args);
        args.AcceptedOperation = copy
            ? DataPackageOperation.Copy
            : DragDropDataService.GetAcceptedInternalMoveOperation(args.DataView);
        args.DragUIOverride.Caption = copy ? $"Copy — {caption}" : caption;
        args.DragUIOverride.IsCaptionVisible = true;
        args.DragUIOverride.IsContentVisible = true;
    }

    private static bool IsCopyRequested(DragEventArgs args) =>
        (args.Modifiers & DragDropModifiers.Control) != 0;

    private static void ShowDropImportStatus(string stackName, int candidateCount, int addedCount)
    {
        var skippedCount = candidateCount - addedCount;
        if (addedCount == 0)
        {
            ShowStatus(
                $"No items were added to {stackName}; the filesystem items are already in this stack.",
                InfoBarSeverity.Informational);
            return;
        }

        var message = skippedCount == 0
            ? addedCount == 1
                ? $"Added 1 item to {stackName}."
                : $"Added {addedCount} items to {stackName}."
            : $"Added {addedCount} {(addedCount == 1 ? "item" : "items")} to {stackName} and skipped " +
              $"{skippedCount} already-present filesystem {(skippedCount == 1 ? "item" : "items")}.";
        ShowStatus(message, InfoBarSeverity.Success);
    }

    private static DropStackViewModel? GetStack(object sender) =>
        (sender as FrameworkElement)?.Tag as DropStackViewModel;

    private static DropStackViewModel? GetTaggedStack(object sender) =>
        (sender as FrameworkElement)?.Tag as DropStackViewModel;

    private static void SetStackDropOutline(object sender, bool isVisible)
    {
        if (sender is FrameworkElement element &&
            element.FindName("StackDropOutline") is Border outline)
        {
            outline.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private static void ShowStatus(string message, InfoBarSeverity severity) =>
        App.Current.ShowToast(message, severity);
}
