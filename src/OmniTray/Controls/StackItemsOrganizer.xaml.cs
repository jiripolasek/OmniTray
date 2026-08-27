// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

using System.Collections.Specialized;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.System;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue;
using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;

namespace OmniTray.Controls;

public sealed partial class StackItemsOrganizer : UserControl
{
    private static readonly TimeSpan CommandHoverDelay = TimeSpan.FromMilliseconds(400);
    private const int ThumbnailColumnCount = 3;

    public static readonly DependencyProperty StackProperty = DependencyProperty.Register(
        nameof(Stack),
        typeof(DropStackViewModel),
        typeof(StackItemsOrganizer),
        new PropertyMetadata(null, OnStackChanged));

    public static readonly DependencyProperty OwnsScrollingProperty = DependencyProperty.Register(
        nameof(OwnsScrolling),
        typeof(bool),
        typeof(StackItemsOrganizer),
        new PropertyMetadata(false, OnOwnsScrollingChanged));

    public static readonly DependencyProperty MaximumListHeightProperty = DependencyProperty.Register(
        nameof(MaximumListHeight),
        typeof(double),
        typeof(StackItemsOrganizer),
        new PropertyMetadata(154d, OnMaximumListHeightChanged));

    public static readonly DependencyProperty StackCardDisplayModeProperty = DependencyProperty.Register(
        nameof(StackCardDisplayMode),
        typeof(OmniTray.Core.StackCardDisplayMode),
        typeof(StackItemsOrganizer),
        new PropertyMetadata(
            OmniTray.Core.StackCardDisplayMode.SmallList,
            OnStackCardDisplayModeChanged));

    private readonly DispatcherQueueTimer _commandHoverTimer;
    private readonly ListInsertionAdornerController _itemInsertionAdorner;
    private FrameworkElement? _commandFlyoutAnchor;
    private DropItemViewModel? _commandTargetItem;
    private FrameworkElement? _hoveredItemRow;
    private bool _isHoverFlyout;
    private bool _isRemovalDialogOpen;
    private bool _isThumbnailView;
    private FrameworkElement? _pendingHoverRow;

    public StackItemsOrganizer()
    {
        this.InitializeComponent();
        this._itemInsertionAdorner = new ListInsertionAdornerController(this.ItemList,
            "ItemInsertionAdorner",
            Orientation.Vertical);
        this._commandHoverTimer = DispatcherQueue
            .GetForCurrentThread()
            .CreateTimer();
        this._commandHoverTimer.Interval = CommandHoverDelay;
        this._commandHoverTimer.IsRepeating = false;
        this._commandHoverTimer.Tick += this.OnCommandHoverTimerTick;
        this.Loaded += this.OnLoaded;
        this.Unloaded += this.OnUnloaded;
    }

    public DropStackViewModel? Stack
    {
        get => (DropStackViewModel?)this.GetValue(StackProperty);
        set => this.SetValue(StackProperty, value);
    }

    public bool OwnsScrolling
    {
        get => (bool)this.GetValue(OwnsScrollingProperty);
        set => this.SetValue(OwnsScrollingProperty, value);
    }

    public double MaximumListHeight
    {
        get => (double)this.GetValue(MaximumListHeightProperty);
        set => this.SetValue(MaximumListHeightProperty, value);
    }

    public OmniTray.Core.StackCardDisplayMode StackCardDisplayMode
    {
        get => (OmniTray.Core.StackCardDisplayMode)this.GetValue(StackCardDisplayModeProperty);
        set => this.SetValue(StackCardDisplayModeProperty, value);
    }

    internal Window? DialogOwner { get; set; }

    internal void SetThumbnailView(bool useThumbnails)
    {
        this.ResetCommandFlyout(true);
        this._isThumbnailView = useThumbnails;
        this.UpdateItemPresentation();
        this._itemInsertionAdorner.SetLayout(
            useThumbnails ? Orientation.Horizontal : Orientation.Vertical,
            useThumbnails);
        if (useThumbnails)
        {
            _ = this.DispatcherQueue.TryEnqueue(this.UpdateThumbnailItemWidth);
        }
    }

    private static void OnStackChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is StackItemsOrganizer organizer)
        {
            if (args.OldValue is DropStackViewModel oldStack)
            {
                oldStack.Items.CollectionChanged -= organizer.OnItemsChanged;
            }

            var newStack = args.NewValue as DropStackViewModel;
            organizer.ItemList.SelectedItems.Clear();
            organizer.ItemList.ItemsSource = newStack?.Items;
            organizer.ResetCommandFlyout(true);
            organizer._itemInsertionAdorner.Clear();
            if (newStack is not null && organizer.IsLoaded)
            {
                newStack.Items.CollectionChanged += organizer.OnItemsChanged;
            }

            organizer.UpdateEmptyState();
            organizer.UpdateSelectionCommands();
        }
    }

    private static void OnOwnsScrollingChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is not StackItemsOrganizer organizer)
        {
            return;
        }

        organizer.ApplyScrollingLayout();
    }

    private static void OnMaximumListHeightChanged(
        DependencyObject sender,
        DependencyPropertyChangedEventArgs args)
    {
        if (sender is StackItemsOrganizer organizer)
        {
            organizer.ApplyScrollingLayout();
        }
    }

    private static void OnStackCardDisplayModeChanged(
        DependencyObject sender,
        DependencyPropertyChangedEventArgs args)
    {
        if (sender is StackItemsOrganizer organizer)
        {
            organizer.UpdateItemPresentation();
        }
    }

    private void UpdateItemPresentation()
    {
        var templateKey = this._isThumbnailView
            ? "ThumbnailItemTemplate"
            : this.StackCardDisplayMode == OmniTray.Core.StackCardDisplayMode.SmallList
                ? "SmallListItemTemplate"
                : "LargeListItemTemplate";
        this.ItemList.ItemTemplate = (DataTemplate)this.Resources[templateKey];
        this.ItemList.ItemsPanel = (ItemsPanelTemplate)this.Resources[
            this._isThumbnailView ? "ThumbnailItemsPanel" : "ListItemsPanel"];
        this.ItemList.ItemContainerStyle = (Style)this.Resources[
            this._isThumbnailView ? "ThumbnailItemContainerStyle" : "ListItemContainerStyle"];
    }

    private void ApplyScrollingLayout()
    {
        var ownsScrolling = this.OwnsScrolling;
        ScrollViewer.SetVerticalScrollMode(this.ItemList,
            ownsScrolling ? ScrollMode.Enabled : ScrollMode.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(this.ItemList,
            ownsScrolling ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled);
        this.ItemList.MaxHeight = ownsScrolling ? this.MaximumListHeight : double.PositiveInfinity;
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        this.UpdateEmptyState();
        this.UpdateSelectionCommands();
        if (this._isThumbnailView)
        {
            _ = this.DispatcherQueue.TryEnqueue(this.UpdateThumbnailItemWidth);
        }
    }

    private void OnItemListSizeChanged(object sender, SizeChangedEventArgs args) =>
        this.UpdateThumbnailItemWidth();

    private void UpdateThumbnailItemWidth()
    {
        if (!this._isThumbnailView ||
            this.ItemList.ItemsPanelRoot is not ItemsWrapGrid itemsPanel ||
            this.ItemList.ActualWidth <= 0)
        {
            return;
        }

        var availableWidth = this.ItemList.ActualWidth -
                             this.ItemList.Padding.Left -
                             this.ItemList.Padding.Right;
        itemsPanel.ItemWidth = Math.Max(96, Math.Floor(availableWidth / ThumbnailColumnCount));
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (this.Stack is not null)
        {
            this.Stack.Items.CollectionChanged -= this.OnItemsChanged;
            this.Stack.Items.CollectionChanged += this.OnItemsChanged;
        }

        this.UpdateEmptyState();
        this.UpdateSelectionCommands();
        if (this._isThumbnailView)
        {
            _ = this.DispatcherQueue.TryEnqueue(this.UpdateThumbnailItemWidth);
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        this.ResetCommandFlyout(true);
        this._itemInsertionAdorner.Clear();
        if (this.Stack is not null)
        {
            this.Stack.Items.CollectionChanged -= this.OnItemsChanged;
        }
    }

    private void OnItemDragItemsStarting(object sender, DragItemsStartingEventArgs args)
    {
        this.ResetCommandFlyout(true);
        if (this.Stack is null)
        {
            args.Cancel = true;
            return;
        }

        var items = args.Items
            .OfType<DropItemViewModel>()
            .Select(static item => item.Model)
            .ToArray();
        if (items.Length == 0)
        {
            args.Cancel = true;
            return;
        }

        DragDropDataService.WriteItems(
            args.Data, this.Stack.Model.Id,
            items,
            items.Length == 1 ? items[0].DisplayName : $"{items.Length} items",
            App.Current.AllowMoveOnDragOutPreference);
    }

    private async void OnItemDragItemsCompleted(object sender, DragItemsCompletedEventArgs args)
    {
        this._itemInsertionAdorner.Clear();
        await App.Current.CompleteItemDragAsync(args.DropResult);
    }

    private void OnItemListDragOver(object sender, DragEventArgs args)
    {
        if (this.Stack is null || !DragDropDataService.HasItemReference(args.DataView))
        {
            return;
        }

        args.Handled = true;
        var target = this._itemInsertionAdorner.Resolve(args.GetPosition(this.ItemList));
        this.ConfigureTransferDragOver(args, target);
    }

    private void OnItemListDragLeave(object sender, DragEventArgs args)
    {
        if (DragDropDataService.HasItemReference(args.DataView))
        {
            this._itemInsertionAdorner.Clear();
        }
    }

    private async void OnItemListDrop(object sender, DragEventArgs args)
    {
        if (this.Stack is null || !DragDropDataService.HasItemReference(args.DataView))
        {
            return;
        }

        args.Handled = true;
        var target = this._itemInsertionAdorner.Resolve(args.GetPosition(this.ItemList));
        this._itemInsertionAdorner.Clear();
        if (target is not null)
        {
            await this.TransferAsync(args, target.Value.InsertionIndex);
        }
    }

    private async Task TransferAsync(DragEventArgs args, int targetIndex)
    {
        var target = this.Stack;
        var itemReference = await DragDropDataService.ReadItemReferenceAsync(args.DataView);
        if (target is null || itemReference is null)
        {
            ShowStatus("Those items are no longer available.", InfoBarSeverity.Warning);
            return;
        }

        var copy = IsCopyRequested(args) && itemReference.SourceStackId != target.Model.Id;
        try
        {
            if (!await App.Current.TransferItemsAsync(itemReference, target, targetIndex, copy))
            {
                ShowStatus("The drop did not change the stack.", InfoBarSeverity.Informational);
                return;
            }

            var itemCount = itemReference.ItemIds.Count;
            ShowStatus(
                $"{(copy ? "Copied" : "Moved")} {itemCount} {(itemCount == 1 ? "item" : "items")} to {target.Name}.",
                InfoBarSeverity.Success);
        }
        catch (Exception exception)
        {
            ShowStatus($"The items could not be organized: {exception.Message}", InfoBarSeverity.Error);
        }
    }

    private void ConfigureTransferDragOver(
        DragEventArgs args,
        ListInsertionTarget? target)
    {
        if (this.Stack is null || target is null || !this.CanApplyActiveItemDrop(target.Value.InsertionIndex))
        {
            this._itemInsertionAdorner.Clear();
            args.AcceptedOperation = DataPackageOperation.None;
            args.DragUIOverride.Caption = "Items are already in this position";
            args.DragUIOverride.IsCaptionVisible = true;
            args.DragUIOverride.IsContentVisible = true;
            return;
        }

        this._itemInsertionAdorner.Show(target.Value);
        var sameStack = DragDropDataService.ActiveItemReference?.SourceStackId == this.Stack.Model.Id;
        var copy = !sameStack && IsCopyRequested(args);
        args.AcceptedOperation = copy
            ? DataPackageOperation.Copy
            : DragDropDataService.GetAcceptedInternalMoveOperation(args.DataView);
        args.DragUIOverride.Caption = sameStack
            ? $"Reorder in {this.Stack.Name}"
            : copy
                ? $"Copy to {this.Stack.Name}"
                : $"Move to {this.Stack.Name}";
        args.DragUIOverride.IsCaptionVisible = true;
        args.DragUIOverride.IsContentVisible = true;
    }

    private bool CanApplyActiveItemDrop(int targetIndex)
    {
        var stack = this.Stack;
        var itemReference = DragDropDataService.ActiveItemReference;
        if (stack is null || itemReference is null || itemReference.SourceStackId != stack.Model.Id)
        {
            return stack is not null;
        }

        try
        {
            var reordered = StackOperations.MoveItemsWithin(
                stack.Model,
                itemReference.ItemIds,
                Math.Clamp(targetIndex, 0, stack.Items.Count));
            return !reordered.Items.Select(static item => item.Id).SequenceEqual(
                stack.Model.Items.Select(static item => item.Id));
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool IsCopyRequested(DragEventArgs args) =>
        (args.Modifiers & DragDropModifiers.Control) != 0;


    private void OnItemRowContextRequested(object sender, ContextRequestedEventArgs args)
    {
        if ((sender as FrameworkElement)?.Tag is not DropItemViewModel item)
        {
            return;
        }

        this._commandHoverTimer.Stop();
        this._pendingHoverRow = null;
        this._isHoverFlyout = false;
        this._commandTargetItem = item;

        if (!this.ItemList.SelectedItems.Contains(item))
        {
            this.ItemList.SelectedItems.Clear();
            this.ItemList.SelectedItem = item;
        }

        this.UpdateSelectionCommands();
    }

    private void OnItemRowPointerEntered(object sender, PointerRoutedEventArgs args)
    {
        if (args.Pointer.PointerDeviceType != PointerDeviceType.Mouse ||
            sender is not FrameworkElement { Tag: DropItemViewModel } row ||
            this._isRemovalDialogOpen ||
            DragDropDataService.ActiveItemReference is not null)
        {
            return;
        }

        this._hoveredItemRow = row;
        if (this.SelectionCommandsFlyout.IsOpen)
        {
            if (!this._isHoverFlyout || ReferenceEquals(this._commandFlyoutAnchor, row))
            {
                return;
            }

            this.SelectionCommandsFlyout.Hide();
        }

        this._pendingHoverRow = row;
        this._commandHoverTimer.Stop();
        this._commandHoverTimer.Start();
    }

    private void OnItemRowPointerExited(object sender, PointerRoutedEventArgs args)
    {
        if (!ReferenceEquals(this._hoveredItemRow, sender))
        {
            return;
        }

        this._hoveredItemRow = null;
        if (ReferenceEquals(this._pendingHoverRow, sender))
        {
            this._pendingHoverRow = null;
            this._commandHoverTimer.Stop();
        }
    }

    private void OnCommandHoverTimerTick(DispatcherQueueTimer sender, object args)
    {
        sender.Stop();
        var row = this._pendingHoverRow;
        this._pendingHoverRow = null;
        if (row is null ||
            !ReferenceEquals(row, this._hoveredItemRow) ||
            row.Tag is not DropItemViewModel item ||
            row.XamlRoot is null || this._isRemovalDialogOpen)
        {
            return;
        }

        this._commandTargetItem = item;
        this._commandFlyoutAnchor = row;
        this._isHoverFlyout = true;
        this.UpdateSelectionCommands();
        this.SelectionCommandsFlyout.ShowAt(
            row,
            new FlyoutShowOptions
            {
                Position = new Point(Math.Max(0, row.ActualWidth - 8), row.ActualHeight / 2),
                ShowMode = FlyoutShowMode.TransientWithDismissOnPointerMoveAway
            });
    }

    private void OnSelectionCommandsFlyoutClosed(object sender, object args)
    {
        this._commandFlyoutAnchor = null;
        this._commandTargetItem = null;
        this._isHoverFlyout = false;
        this.UpdateSelectionCommands();
    }

    private void OnItemSelectionChanged(object sender, SelectionChangedEventArgs args) =>
        this.UpdateSelectionCommands();

    private void OnMoveSelectionUpClick(object sender, RoutedEventArgs args) => this.MoveSelection(-1);

    private void OnMoveSelectionDownClick(object sender, RoutedEventArgs args) => this.MoveSelection(1);

    private void MoveSelection(int direction)
    {
        var selectedIds = this.GetCommandItemIds();
        if (this.Stack is not null && this.Stack.MoveItems(selectedIds, direction))
        {
            ShowStatus(
                selectedIds.Length == 1 ? "Moved 1 item." : $"Moved {selectedIds.Length} items.",
                InfoBarSeverity.Success);
        }

        this.UpdateSelectionCommands();
    }

    private void OnSplitSelectionClick(object sender, RoutedEventArgs args)
    {
        var selectedIds = this.GetCommandItemIds();
        if (this.Stack is null || selectedIds.Length == 0 || selectedIds.Length == this.Stack.Items.Count)
        {
            return;
        }

        App.Current.SplitStack(this.Stack, selectedIds);
        this.ItemList.SelectedItems.Clear();
        ShowStatus(
            selectedIds.Length == 1
                ? "Split 1 item into a new stack."
                : $"Split {selectedIds.Length} items into a new stack.",
            InfoBarSeverity.Success);
    }

    private async void OnDuplicateSelectionClick(object sender, RoutedEventArgs args)
    {
        var stack = this.Stack;
        var selectedIds = this.GetCommandItemIds();
        if (stack is null || selectedIds.Length == 0)
        {
            return;
        }

        var selected = selectedIds.ToHashSet();
        var lastSelectedIndex = stack.Items
            .Select((item, index) => (item, index))
            .Where(value => selected.Contains(value.item.Model.Id))
            .Select(static value => value.index)
            .DefaultIfEmpty(stack.Items.Count - 1)
            .Max();
        try
        {
            if (await App.Current.TransferItemsAsync(
                    new ItemDragReference(stack.Model.Id, selectedIds),
                    stack,
                    Math.Min(lastSelectedIndex + 1, stack.Items.Count),
                    true,
                    true))
            {
                ShowStatus(
                    selectedIds.Length == 1 ? "Duplicated 1 item." : $"Duplicated {selectedIds.Length} items.",
                    InfoBarSeverity.Success);
            }
        }
        catch (Exception exception)
        {
            ShowStatus($"The items could not be duplicated: {exception.Message}", InfoBarSeverity.Error);
        }
    }

    private async void OnOpenSelectionClick(object sender, RoutedEventArgs args)
    {
        var item = this.GetCommandItems().SingleOrDefault();
        if (item is null)
        {
            return;
        }

        try
        {
            await ItemManipulationService.OpenAsync(item);
        }
        catch (Exception exception)
        {
            ShowStatus(exception.Message, InfoBarSeverity.Error);
        }
    }

    private async void OnOpenContainingFolderClick(object sender, RoutedEventArgs args)
    {
        var item = this.GetCommandItems().SingleOrDefault();
        if (item is null)
        {
            return;
        }

        try
        {
            await ItemManipulationService.OpenContainingFolderAsync(item);
        }
        catch (Exception exception)
        {
            ShowStatus(exception.Message, InfoBarSeverity.Error);
        }
    }

    private async void OnOpenSourceUrlClick(object sender, RoutedEventArgs args)
    {
        var item = this.GetCommandItems().SingleOrDefault();
        if (item is null)
        {
            return;
        }

        try
        {
            await ItemManipulationService.OpenSourceUrlAsync(item);
        }
        catch (Exception exception)
        {
            ShowStatus(exception.Message, InfoBarSeverity.Error);
        }
    }

    private void OnCopySelectionClick(object sender, RoutedEventArgs args)
    {
        var items = this.GetCommandItems();
        if (items.Length == 0)
        {
            return;
        }

        try
        {
            ItemManipulationService.PutOnClipboard(items, DataPackageOperation.Copy);
            ShowStatus(
                items.Length == 1 ? "Copied 1 item." : $"Copied {items.Length} items.",
                InfoBarSeverity.Success);
        }
        catch (Exception exception)
        {
            ShowStatus($"The items could not be copied: {exception.Message}", InfoBarSeverity.Error);
        }
    }

    private void OnCutSelectionClick(object sender, RoutedEventArgs args)
    {
        var items = this.GetCommandItems();
        if (items.Length == 0)
        {
            return;
        }

        try
        {
            ItemManipulationService.PutOnClipboard(items, DataPackageOperation.Move);
            ShowStatus(
                items.Length == 1 ? "Cut 1 item." : $"Cut {items.Length} items.",
                InfoBarSeverity.Success);
        }
        catch (Exception exception)
        {
            ShowStatus($"The items could not be cut: {exception.Message}", InfoBarSeverity.Error);
        }
    }

    private void OnShowPropertiesClick(object sender, RoutedEventArgs args)
    {
        var item = this.GetCommandItems().SingleOrDefault();
        if (item is null)
        {
            return;
        }

        try
        {
            ItemManipulationService.ShowProperties(item);
        }
        catch (Exception exception)
        {
            ShowStatus(exception.Message, InfoBarSeverity.Error);
        }
    }

    private void OnInspectPayloadClick(object sender, RoutedEventArgs args)
    {
        var item = this.GetCommandItems().SingleOrDefault();
        if (item is not null)
        {
            App.Current.ShowDataFormatInspector(item);
        }
    }

    private async void OnDeleteFromDiskClick(object sender, RoutedEventArgs args)
    {
        var source = this.Stack;
        var requestedItems = this.GetCommandItems();
        var dialogOwner = this.DialogOwner;
        var xamlRoot = this.XamlRoot;
        if (source is null ||
            requestedItems.Length == 0 ||
            (dialogOwner is null && xamlRoot is null) ||
            this._isRemovalDialogOpen)
        {
            return;
        }

        this._isRemovalDialogOpen = true;
        this.UpdateSelectionCommands();
        try
        {
            await this.CloseSelectionCommandsFlyoutAsync();
            var confirmed = dialogOwner is not null
                ? await StackDialogService.ConfirmRecycleItemsAsync(dialogOwner, requestedItems.Length)
                : await StackDialogService.ConfirmRecycleItemsAsync(xamlRoot!, requestedItems.Length);
            if (!confirmed || !App.Current.StackCatalogViewModel.Stacks.Contains(source))
            {
                return;
            }

            var requestedIds = requestedItems.Select(static item => item.Id).ToHashSet();
            var existingItems = source.Model.Items
                .Where(item => requestedIds.Contains(item.Id))
                .ToArray();
            var result = await ItemManipulationService.RecycleAsync(existingItems);
            if (result.DeletedItemIds.Count > 0)
            {
                await App.Current.RemoveItemsAsync(source, result.DeletedItemIds);
            }

            if (result.FailedCount == 0)
            {
                ShowStatus(
                    result.DeletedItemIds.Count == 1
                        ? "Moved 1 item to the Recycle Bin."
                        : $"Moved {result.DeletedItemIds.Count} items to the Recycle Bin.",
                    InfoBarSeverity.Success);
            }
            else
            {
                ShowStatus(
                    result.DeletedItemIds.Count == 0
                        ? result.ErrorMessage ?? "The items could not be deleted."
                        : $"Deleted {result.DeletedItemIds.Count} items; {result.FailedCount} failed.",
                    result.DeletedItemIds.Count == 0 ? InfoBarSeverity.Error : InfoBarSeverity.Warning);
            }
        }
        finally
        {
            this._isRemovalDialogOpen = false;
            this.UpdateSelectionCommands();
        }
    }

    private async void OnRemoveSelectionClick(object sender, RoutedEventArgs args)
    {
        var selectedIds = this.GetCommandItemIds();
        if (this.Stack is not null && selectedIds.Length > 0)
        {
            await this.RequestRemovalAsync(this.Stack, selectedIds);
        }
    }

    private async void OnItemListKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key != VirtualKey.Delete || this.Stack is null)
        {
            return;
        }

        var selectedIds = this.GetSelectedItemIds();
        if (selectedIds.Length > 0)
        {
            args.Handled = true;
            await this.RequestRemovalAsync(this.Stack, selectedIds);
        }
    }

    private async Task RequestRemovalAsync(DropStackViewModel source, IEnumerable<Guid> itemIds)
    {
        var requested = itemIds.ToHashSet();
        var requestedIds = source.Model.Items
            .Where(item => requested.Contains(item.Id))
            .Select(static item => item.Id)
            .ToArray();
        var dialogOwner = this.DialogOwner;
        var xamlRoot = this.XamlRoot;
        if (requestedIds.Length == 0)
        {
            ShowStatus("Those items are no longer available.", InfoBarSeverity.Warning);
            return;
        }

        if ((dialogOwner is null && xamlRoot is null) || this._isRemovalDialogOpen)
        {
            return;
        }

        this._isRemovalDialogOpen = true;
        this.UpdateSelectionCommands();
        try
        {
            await this.CloseSelectionCommandsFlyoutAsync();
            var confirmed = dialogOwner is not null
                ? await StackDialogService.ConfirmRemoveItemsAsync(dialogOwner, source, requestedIds.Length)
                : await StackDialogService.ConfirmRemoveItemsAsync(xamlRoot!, source, requestedIds.Length);
            if (!confirmed ||
                !App.Current.StackCatalogViewModel.Stacks.Contains(source))
            {
                return;
            }

            var existingIds = source.Model.Items
                .Where(item => requested.Contains(item.Id))
                .Select(static item => item.Id)
                .ToArray();
            if (existingIds.Length == 0)
            {
                ShowStatus("Those items are no longer available.", InfoBarSeverity.Warning);
                return;
            }

            await App.Current.RemoveItemsAsync(source, existingIds);
            ShowStatus(
                existingIds.Length == 1 ? "Removed 1 item." : $"Removed {existingIds.Length} items.",
                InfoBarSeverity.Success);
            this.UpdateEmptyState();
        }
        finally
        {
            this._isRemovalDialogOpen = false;
            this.UpdateSelectionCommands();
        }
    }

    private Task CloseSelectionCommandsFlyoutAsync()
    {
        if (!this.SelectionCommandsFlyout.IsOpen)
        {
            this.ResetCommandFlyout(true);
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource<object?>();
        void OnClosed(object? sender, object args)
        {
            this.SelectionCommandsFlyout.Closed -= OnClosed;
            completion.TrySetResult(null);
        }

        this.SelectionCommandsFlyout.Closed += OnClosed;
        this.ResetCommandFlyout(true);
        return completion.Task;
    }

    private void UpdateEmptyState()
    {
        var isEmpty = this.Stack is null || this.Stack.Items.Count == 0;
        this.ItemList.Visibility = isEmpty ? Visibility.Collapsed : Visibility.Visible;
        this.EmptyMessage.Visibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;
    }

    private Guid[] GetSelectedItemIds() =>
        this.ItemList.SelectedItems
            .OfType<DropItemViewModel>()
            .Select(static item => item.Model.Id)
            .ToArray();

    private Guid[] GetCommandItemIds()
    {
        if (this._commandTargetItem is { } target && this.Stack?.Items.Contains(target) == true &&
            !this.ItemList.SelectedItems.Contains(target))
        {
            return [target.Model.Id];
        }

        return this.GetSelectedItemIds();
    }

    private DropItem[] GetCommandItems()
    {
        var selectedIds = this.GetCommandItemIds().ToHashSet();
        return this.Stack?.Model.Items
                   .Where(item => selectedIds.Contains(item.Id))
                   .ToArray() ?? [];
    }

    private void ResetCommandFlyout(bool hideFlyout)
    {
        this._commandHoverTimer.Stop();
        this._hoveredItemRow = null;
        this._pendingHoverRow = null;
        this._commandFlyoutAnchor = null;
        this._commandTargetItem = null;
        this._isHoverFlyout = false;
        if (hideFlyout)
        {
            this.SelectionCommandsFlyout.Hide();
        }
    }

    private void UpdateSelectionCommands()
    {
        var selectedIds = this.GetCommandItemIds();
        var selectedItems = this.GetCommandItems();
        var hasSelection = this.Stack is not null && selectedIds.Length > 0;
        var singleItem = selectedItems.Length == 1 ? selectedItems[0] : null;
        var singleActions = singleItem is null
            ? ContentActions.None
            : ContentMetadataPolicy.GetMetadata(singleItem).Actions;
        this.MoveSelectionUpButton.IsEnabled = hasSelection && this.Stack!.CanMoveItems(selectedIds, -1);
        this.MoveSelectionDownButton.IsEnabled = hasSelection && this.Stack!.CanMoveItems(selectedIds, 1);
        this.SplitSelectionButton.IsEnabled = hasSelection && selectedIds.Length < this.Stack!.Items.Count;
        this.RemoveSelectionButton.IsEnabled = hasSelection && !this._isRemovalDialogOpen;
        this.DuplicateSelectionButton.IsEnabled = hasSelection;
        this.OpenSelectionButton.Visibility = Has(singleActions, ContentActions.Open)
            ? Visibility.Visible
            : Visibility.Collapsed;
        this.OpenContainingFolderButton.Visibility = Has(
            singleActions,
            ContentActions.Reveal)
            ? Visibility.Visible
            : Visibility.Collapsed;
        this.OpenSourceUrlButton.Visibility = Has(singleActions, ContentActions.OpenSource) &&
                                              (!string.IsNullOrWhiteSpace(singleItem?.SourceApplicationLink) ||
                                               singleItem?.Kind != DropItemKind.Uri ||
                                               !string.Equals(
                                                   singleItem.Url,
                                                   singleItem.SourceUrl,
                                                   StringComparison.OrdinalIgnoreCase))
            ? Visibility.Visible
            : Visibility.Collapsed;
        this.CopySelectionButton.IsEnabled = hasSelection && selectedItems.All(static item =>
            ContentMetadataPolicy.HasAction(item, ContentActions.Copy));
        this.CutSelectionButton.Visibility = hasSelection && selectedItems.All(static item =>
            ContentMetadataPolicy.HasAction(item, ContentActions.Cut))
            ? Visibility.Visible
            : Visibility.Collapsed;
        this.ShowPropertiesButton.Visibility = Has(singleActions, ContentActions.ShowProperties)
            ? Visibility.Visible
            : Visibility.Collapsed;
        this.InspectPayloadButton.Visibility = singleItem is null
            ? Visibility.Collapsed
            : Visibility.Visible;
        this.DeleteFromDiskButton.Visibility = hasSelection && selectedItems.All(static item =>
            ContentMetadataPolicy.HasAction(item, ContentActions.Delete))
            ? Visibility.Visible
            : Visibility.Collapsed;
        this.DeleteFromDiskButton.IsEnabled = !this._isRemovalDialogOpen;
    }

    private static bool Has(ContentActions actions, ContentActions requested) =>
        (actions & requested) == requested;

    private static void ShowStatus(string message, InfoBarSeverity severity) =>
        App.Current.ShowToast(message, severity);
}
