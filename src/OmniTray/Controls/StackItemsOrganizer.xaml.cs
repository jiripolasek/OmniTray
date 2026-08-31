// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

using System.Collections.Specialized;
using System.ComponentModel;
using System.Numerics;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.System;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using OmniTray.ViewModels.Organizer;
using DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue;
using DispatcherQueuePriority = Microsoft.UI.Dispatching.DispatcherQueuePriority;
using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;

namespace OmniTray.Controls;

public sealed partial class StackItemsOrganizer : UserControl
{
    private static readonly TimeSpan CommandHoverDelay = TimeSpan.FromMilliseconds(400);
    private static readonly string[] OrganizerItemChromeResourceKeys =
    [
        "ListViewItemBackgroundPointerOver",
        "ListViewItemBackgroundPressed",
        "ListViewItemBackgroundSelected",
        "ListViewItemBackgroundSelectedPointerOver",
        "ListViewItemBackgroundSelectedPressed",
        "ListViewItemPointerOverBorderBrush",
        "ListViewItemSelectedBorderBrush",
        "ListViewItemSelectedPointerOverBorderBrush",
        "ListViewItemSelectedPressedBorderBrush",
        "ListViewItemSelectedInnerBorderBrush"
    ];
    private const float ItemHoverElevation = 16;

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

    public static readonly DependencyProperty ThumbnailItemWidthProperty = DependencyProperty.Register(
        nameof(ThumbnailItemWidth),
        typeof(double),
        typeof(StackItemsOrganizer),
        new PropertyMetadata(double.NaN, OnThumbnailItemWidthChanged));

    public static readonly DependencyProperty StackCardDisplayModeProperty = DependencyProperty.Register(
        nameof(StackCardDisplayMode),
        typeof(OmniTray.Core.StackCardDisplayMode),
        typeof(StackItemsOrganizer),
        new PropertyMetadata(
            OmniTray.Core.StackCardDisplayMode.SmallList,
            OnStackCardDisplayModeChanged));

    public static readonly DependencyProperty ShowCommandFlyoutOnHoverProperty = DependencyProperty.Register(
        nameof(ShowCommandFlyoutOnHover),
        typeof(bool),
        typeof(StackItemsOrganizer),
        new PropertyMetadata(true, OnShowCommandFlyoutOnHoverChanged));

    public static readonly DependencyProperty UseOrganizerCardPresentationProperty = DependencyProperty.Register(
        nameof(UseOrganizerCardPresentation),
        typeof(bool),
        typeof(StackItemsOrganizer),
        new PropertyMetadata(false, OnUseOrganizerCardPresentationChanged));

    private readonly DispatcherQueueTimer _commandHoverTimer;
    private readonly ListInsertionAdornerController _itemInsertionAdorner;
    private FrameworkElement? _commandFlyoutAnchor;
    private DropItemViewModel? _commandTargetItem;
    private FrameworkElement? _hoveredItemRow;
    private bool _isHoverFlyout;
    private bool _isRemovalDialogOpen;
    private bool _isThumbnailView;
    private OrganizerCollectionViewMode _organizerViewMode = OrganizerCollectionViewMode.Medium;
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

    // A preferred width lets the wrap panel choose the column count. NaN preserves the compact three-column layout.
    public double ThumbnailItemWidth
    {
        get => (double)this.GetValue(ThumbnailItemWidthProperty);
        set => this.SetValue(ThumbnailItemWidthProperty, value);
    }

    public OmniTray.Core.StackCardDisplayMode StackCardDisplayMode
    {
        get => (OmniTray.Core.StackCardDisplayMode)this.GetValue(StackCardDisplayModeProperty);
        set => this.SetValue(StackCardDisplayModeProperty, value);
    }

    public bool ShowCommandFlyoutOnHover
    {
        get => (bool)this.GetValue(ShowCommandFlyoutOnHoverProperty);
        set => this.SetValue(ShowCommandFlyoutOnHoverProperty, value);
    }

    public bool UseOrganizerCardPresentation
    {
        get => (bool)this.GetValue(UseOrganizerCardPresentationProperty);
        set => this.SetValue(UseOrganizerCardPresentationProperty, value);
    }

    internal Window? DialogOwner { get; set; }

    internal DropItemViewModel? PrimarySelectedItem =>
        this.ItemList.SelectedItems.OfType<DropItemViewModel>().FirstOrDefault();

    internal int SelectedItemCount => this.ItemList.SelectedItems.Count;

    internal event EventHandler? SelectedItemsChanged;
    internal event EventHandler? SelectionCommandsChanged;

    internal bool CanOpenSelectedItem => this.GetSelectedItems() is [var item] &&
                                         Has(ContentMetadataPolicy.GetMetadata(item).Actions,
                                             ContentActions.Open);

    internal bool CanOpenSelectedItemContainer => this.GetSelectedItems() is [var item] &&
                                                  Has(ContentMetadataPolicy.GetMetadata(item).Actions,
                                                      ContentActions.Reveal);

    internal bool CanCopySelectedItems => this.GetSelectedItems() is { Length: > 0 } items &&
                                          items.All(static item =>
                                              ContentMetadataPolicy.HasAction(item, ContentActions.Copy));

    internal bool CanCutSelectedItems => this.GetSelectedItems() is { Length: > 0 } items &&
                                         items.All(static item =>
                                             ContentMetadataPolicy.HasAction(item, ContentActions.Cut));

    internal bool CanDeleteSelectedItemsFromDisk => this.GetSelectedItems() is { Length: > 0 } items &&
                                                    items.All(static item =>
                                                        ContentMetadataPolicy.HasAction(item,
                                                            ContentActions.Delete));

    internal bool CanMoveSelectedItemsUp => this.Stack is { } stack &&
                                            stack.CanMoveItems(this.GetSelectedItemIds(), -1);

    internal bool CanMoveSelectedItemsDown => this.Stack is { } stack &&
                                              stack.CanMoveItems(this.GetSelectedItemIds(), 1);

    internal bool CanSplitSelectedItems => this.Stack is { } stack &&
                                           this.SelectedItemCount > 0 &&
                                           this.SelectedItemCount < stack.Items.Count;

    internal bool CanChangeSelectedItems => this.SelectedItemCount > 0 && !this._isRemovalDialogOpen;

    internal bool SelectItem(Guid itemId)
    {
        var stack = this.Stack;
        var item = stack?.Items.FirstOrDefault(candidate => candidate.Model.Id == itemId);
        if (item is null)
        {
            return false;
        }

        this.ItemList.SelectedItems.Clear();
        this.ItemList.SelectedItem = item;
        this.ItemList.ScrollIntoView(item);
        _ = this.DispatcherQueue.TryEnqueue(() =>
        {
            if (this.IsLoaded && ReferenceEquals(this.Stack, stack) && ReferenceEquals(this.PrimarySelectedItem, item))
            {
                this.ItemList.UpdateLayout();
                this.ItemList.ScrollIntoView(item);
                (this.ItemList.ContainerFromItem(item) as Control)?.Focus(FocusState.Programmatic);
            }
        });
        return true;
    }

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
            this.QueueThumbnailLayoutRefresh();
        }
    }

    private static void OnStackChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is StackItemsOrganizer organizer)
        {
            if (args.OldValue is DropStackViewModel oldStack)
            {
                oldStack.Items.CollectionChanged -= organizer.OnItemsChanged;
                oldStack.ModelChanged -= organizer.OnStackModelChangedForNotes;
                oldStack.PropertyChanged -= organizer.OnStackPropertyChangedForItems;
            }

            var newStack = args.NewValue as DropStackViewModel;
            using var operation = App.Current.TrackUiOperation(newStack is null
                ? "Clear stack item binding"
                : $"Bind stack '{newStack.Name}' ({newStack.Items.Count:N0} items)");
            organizer.ItemList.SelectedItems.Clear();
            organizer.ItemList.ItemsSource = newStack?.Items;
            organizer.ResetCommandFlyout(true);
            organizer._itemInsertionAdorner.Clear();
            if (newStack is not null && organizer.IsLoaded)
            {
                newStack.Items.CollectionChanged += organizer.OnItemsChanged;
                newStack.ModelChanged += organizer.OnStackModelChangedForNotes;
                newStack.PropertyChanged += organizer.OnStackPropertyChangedForItems;
            }

            organizer.UpdateEmptyState();
            organizer.UpdateSelectionCommands();
            organizer.UpdateSelectionIndicators();
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

    private static void OnThumbnailItemWidthChanged(
        DependencyObject sender,
        DependencyPropertyChangedEventArgs args)
    {
        if (sender is StackItemsOrganizer organizer)
        {
            organizer.UpdateThumbnailItemWidth();
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

    private static void OnShowCommandFlyoutOnHoverChanged(
        DependencyObject sender,
        DependencyPropertyChangedEventArgs args)
    {
        if (sender is not StackItemsOrganizer organizer)
        {
            return;
        }

        var showOnHover = (bool)args.NewValue;
        AutomationProperties.SetHelpText(
            organizer.ItemList,
            showOnHover
                ? "Hover with a mouse, or use the context menu, to show commands. Press Space to toggle selection. Drag to reorder or move; hold Control while dragging to copy."
                : "Use the context menu to show item commands. Press Space to toggle selection. Drag to reorder or move; hold Control while dragging to copy.");
        if (showOnHover)
        {
            return;
        }

        organizer._commandHoverTimer.Stop();
        organizer._pendingHoverRow = null;
        if (organizer._isHoverFlyout)
        {
            organizer.SelectionCommandsFlyout.Hide();
        }
    }

    internal void SetOrganizerViewMode(OrganizerCollectionViewMode viewMode)
    {
        this.ResetCommandFlyout(true);
        this._organizerViewMode = viewMode;
        this._isThumbnailView = viewMode != OrganizerCollectionViewMode.List;
        this.UpdateItemPresentation();
        this._itemInsertionAdorner.SetLayout(
            this._isThumbnailView ? Orientation.Horizontal : Orientation.Vertical,
            this._isThumbnailView);
        if (this._isThumbnailView)
        {
            this.QueueThumbnailLayoutRefresh();
        }
    }

    private static void OnUseOrganizerCardPresentationChanged(
        DependencyObject sender,
        DependencyPropertyChangedEventArgs args)
    {
        if (sender is StackItemsOrganizer organizer)
        {
            organizer.ResetCommandFlyout(true);
            organizer.UpdateItemPresentation();
            organizer.UpdateKeyboardShortcutText();
        }
    }

    private void UpdateKeyboardShortcutText()
    {
        var showOrganizerShortcuts = this.UseOrganizerCardPresentation;
        this.OpenContainingFolderButton.KeyboardAcceleratorTextOverride =
            showOrganizerShortcuts ? "Ctrl+E" : string.Empty;
        this.CopySelectionButton.KeyboardAcceleratorTextOverride =
            showOrganizerShortcuts ? "Ctrl+C" : string.Empty;
    }

    private void UpdateItemPresentation()
    {
        var useOrganizerPresentation = this.UseOrganizerCardPresentation;
        var useOrganizerGrid = this._isThumbnailView && useOrganizerPresentation;
        var templateKey = this._isThumbnailView
            ? useOrganizerGrid ? "OrganizerThumbnailItemTemplate" : "ThumbnailItemTemplate"
            : useOrganizerPresentation
                ? "OrganizerListItemTemplate"
            : this.StackCardDisplayMode == OmniTray.Core.StackCardDisplayMode.SmallList
                ? "SmallListItemTemplate"
                : "LargeListItemTemplate";
        this.UpdateItemContainerChrome(useOrganizerPresentation);
        this.ItemList.ItemTemplate = (DataTemplate)this.Resources[templateKey];
        this.ItemList.ItemsPanel = (ItemsPanelTemplate)this.Resources[
            this._isThumbnailView
                ? useOrganizerGrid ? "OrganizerThumbnailItemsPanel" : "ThumbnailItemsPanel"
                : "ListItemsPanel"];
        this.ItemList.ItemContainerStyle = (Style)this.Resources[
            this._isThumbnailView
                ? useOrganizerGrid ? "OrganizerThumbnailItemContainerStyle" : "ThumbnailItemContainerStyle"
                : useOrganizerPresentation ? "OrganizerListItemContainerStyle"
                : "ListItemContainerStyle"];
        _ = this.DispatcherQueue.TryEnqueue(this.UpdateSelectionIndicators);
    }

    private void UpdateItemContainerChrome(bool useOrganizerPresentation)
    {
        if (useOrganizerPresentation)
        {
            var transparentBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            foreach (var key in OrganizerItemChromeResourceKeys)
            {
                this.ItemList.Resources[key] = transparentBrush;
            }

            return;
        }

        foreach (var key in OrganizerItemChromeResourceKeys)
        {
            this.ItemList.Resources.Remove(key);
        }
    }

    private void ApplyScrollingLayout()
    {
        var ownsScrolling = this.OwnsScrolling;
        ScrollViewer.SetVerticalScrollMode(this.ItemList,
            ownsScrolling ? ScrollMode.Enabled : ScrollMode.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(this.ItemList,
            ownsScrolling ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled);
        this.ItemsHostRow.Height = ownsScrolling
            ? new GridLength(1, GridUnitType.Star)
            : GridLength.Auto;
        this.ItemList.MaxHeight = ownsScrolling ? this.MaximumListHeight : double.PositiveInfinity;
        this.UpdateEmptyState();
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        this.UpdateEmptyState();
        this.UpdateSelectionCommands();
        this.UpdateSelectionIndicators();
        if (this._isThumbnailView)
        {
            _ = this.DispatcherQueue.TryEnqueue(this.UpdateThumbnailItemWidth);
        }
    }

    private void OnStackPropertyChangedForItems(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(DropStackViewModel.Items) ||
            sender is not DropStackViewModel stack ||
            !ReferenceEquals(stack, this.Stack))
        {
            return;
        }

        using var operation = App.Current.TrackUiOperation(
            $"Rebind stack '{stack.Name}' ({stack.Items.Count:N0} items)");
        this.ItemList.ItemsSource = stack.Items;
        stack.Items.CollectionChanged -= this.OnItemsChanged;
        stack.Items.CollectionChanged += this.OnItemsChanged;
        this.OnItemsChanged(stack.Items,
            new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    private void OnItemListSizeChanged(object sender, SizeChangedEventArgs args) =>
        this.UpdateThumbnailItemWidth();

    private void QueueThumbnailLayoutRefresh()
    {
        _ = this.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (!this._isThumbnailView || !this.IsLoaded)
            {
                return;
            }

            using var operation = App.Current.TrackUiOperation("Refresh thumbnail layout");

            // A view-mode change can reuse the same ItemsPanelTemplate. Materialize the current
            // panel first, then apply its new dimensions and consume that invalidation now instead
            // of waiting for a pointer/focus event to trigger another layout pass.
            this.ItemList.InvalidateMeasure();
            this.ItemList.UpdateLayout();
            this.UpdateThumbnailItemWidth();
            this.ItemList.UpdateLayout();
        });
    }

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
        var preferredItemWidth = this.UseOrganizerCardPresentation
            ? this._organizerViewMode switch
            {
                OrganizerCollectionViewMode.Small => 160,
                OrganizerCollectionViewMode.Large => 300,
                _ => 220
            }
            : this.ThumbnailItemWidth;
        var itemWidth = StackThumbnailLayout.GetItemWidth(availableWidth, preferredItemWidth);
        var layoutChanged = false;
        if (itemWidth > 0 && itemsPanel.ItemWidth != itemWidth)
        {
            itemsPanel.ItemWidth = itemWidth;
            layoutChanged = true;
        }

        if (this.UseOrganizerCardPresentation)
        {
            var itemHeight = this._organizerViewMode switch
            {
                OrganizerCollectionViewMode.Small => 190,
                OrganizerCollectionViewMode.Large => 290,
                _ => 230
            };
            if (itemsPanel.ItemHeight != itemHeight)
            {
                itemsPanel.ItemHeight = itemHeight;
                layoutChanged = true;
            }
        }

        if (layoutChanged)
        {
            itemsPanel.InvalidateMeasure();
            this.ItemList.InvalidateMeasure();
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        using var operation = App.Current.TrackUiOperation(this.Stack is { } stack
            ? $"Load stack '{stack.Name}' ({stack.Items.Count:N0} items)"
            : "Load empty stack item view");
        if (this.Stack is not null)
        {
            this.ItemList.ItemsSource = this.Stack.Items;
            this.Stack.Items.CollectionChanged -= this.OnItemsChanged;
            this.Stack.Items.CollectionChanged += this.OnItemsChanged;
            this.Stack.ModelChanged -= this.OnStackModelChangedForNotes;
            this.Stack.ModelChanged += this.OnStackModelChangedForNotes;
            this.Stack.PropertyChanged -= this.OnStackPropertyChangedForItems;
            this.Stack.PropertyChanged += this.OnStackPropertyChangedForItems;
        }

        this.UpdateEmptyState();
        this.UpdateSelectionCommands();
        this.UpdateSelectionIndicators();
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
            this.Stack.ModelChanged -= this.OnStackModelChangedForNotes;
            this.Stack.PropertyChanged -= this.OnStackPropertyChangedForItems;
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
            App.Current.AllowMoveOnDragOutPreference && this.Stack.CanRemoveItems);
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
        if (this.Stack is null ||
            !this.Stack.CanWriteItems ||
            target is null ||
            !this.CanApplyActiveItemDrop(target.Value.InsertionIndex))
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

        if (!stack.CanManuallyReorderItems)
        {
            return false;
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
            sender is not FrameworkElement { Tag: DropItemViewModel } row)
        {
            return;
        }

        if (this._hoveredItemRow is { } previousRow && !ReferenceEquals(previousRow, row))
        {
            this.UpdateItemHoverShadow(previousRow, false);
            this.UpdateSelectionIndicator(previousRow, false);
        }

        this._hoveredItemRow = row;
        this.UpdateSelectionIndicator(row, true);
        this.UpdateItemHoverShadow(row, true);
        if (!this.ShowCommandFlyoutOnHover ||
            this._isRemovalDialogOpen ||
            DragDropDataService.ActiveItemReference is not null)
        {
            return;
        }

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
        if (sender is FrameworkElement row)
        {
            this.UpdateItemHoverShadow(row, false);
            this.UpdateSelectionIndicator(row, false);
        }

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
        if (!this.ShowCommandFlyoutOnHover ||
            row is null ||
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

    internal void FocusItemList() => this.ItemList.Focus(FocusState.Keyboard);

    internal void ClearSelection() => this.ItemList.SelectedItems.Clear();

    private void OnSelectionCheckBoxLoaded(object sender, RoutedEventArgs args)
    {
        if (sender is CheckBox checkBox)
        {
            this.UpdateSelectionCheckBox(checkBox, ReferenceEquals(this.FindItemRow(checkBox), this._hoveredItemRow));
        }
    }

    private void OnSelectionCheckBoxPointerPressed(object sender, PointerRoutedEventArgs args) =>
        args.Handled = true;

    private void OnSelectionCheckBoxClick(object sender, RoutedEventArgs args)
    {
        if (sender is not CheckBox { Tag: DropItemViewModel item } checkBox ||
            this.Stack?.Items.Contains(item) != true)
        {
            return;
        }

        if (checkBox.IsChecked == true)
        {
            if (!this.ItemList.SelectedItems.Contains(item))
            {
                this.ItemList.SelectedItems.Add(item);
            }
        }
        else
        {
            this.ItemList.SelectedItems.Remove(item);
        }

        this.UpdateSelectionIndicators();
    }

    private void OnItemSelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        this.UpdateSelectionCommands();
        this.UpdateSelectionIndicators();
        this.SelectedItemsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateSelectionIndicators()
    {
        foreach (var checkBox in FindDescendants<CheckBox>(this.ItemList)
                     .Where(static candidate => candidate.Name == "ItemSelectionCheckBox"))
        {
            var row = this.FindItemRow(checkBox);
            this.UpdateSelectionCheckBox(checkBox, ReferenceEquals(row, this._hoveredItemRow));
        }
    }

    private void UpdateSelectionIndicator(FrameworkElement row, bool isHovered)
    {
        var checkBox = FindDescendants<CheckBox>(row)
            .FirstOrDefault(static candidate => candidate.Name == "ItemSelectionCheckBox");
        if (checkBox is not null)
        {
            this.UpdateSelectionCheckBox(checkBox, isHovered);
        }
    }

    private void UpdateSelectionCheckBox(CheckBox checkBox, bool isHovered)
    {
        if (checkBox.Tag is not DropItemViewModel item)
        {
            return;
        }

        var isSelected = this.ItemList.SelectedItems.Contains(item);
        checkBox.IsChecked = isSelected;
        checkBox.Visibility = this.SelectedItemCount > 0 || isHovered
            ? Visibility.Visible
            : Visibility.Collapsed;

        var row = this.FindItemRow(checkBox);
        var border = row is null
            ? null
            : FindDescendants<Border>(row)
                .FirstOrDefault(static candidate => candidate.Name == "ItemSelectionBorder");
        if (border is not null)
        {
            border.Visibility = isSelected ? Visibility.Visible : Visibility.Collapsed;
        }

        var surface = row is null
            ? null
            : FindDescendants<Border>(row)
                .FirstOrDefault(static candidate => candidate.Name == "ItemCardSurface");
        if (surface is not null)
        {
            surface.Opacity = isSelected || isHovered ? 1 : 0;
        }
    }

    private FrameworkElement? FindItemRow(DependencyObject child)
    {
        var current = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(child);
        while (current is not null && current != this.ItemList)
        {
            if (current is FrameworkElement { Tag: DropItemViewModel })
            {
                return (FrameworkElement)current;
            }

            current = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private void UpdateItemHoverShadow(DependencyObject row, bool isHovered)
    {
        var container = this.FindItemContainer(row);
        var surface = FindDescendants<Border>(row)
            .FirstOrDefault(static candidate => candidate.Name == "ItemCardSurface");
        if (container is null || surface is null)
        {
            return;
        }

        var translation = surface.Translation;
        surface.Translation = new Vector3(
            translation.X,
            translation.Y,
            isHovered ? ItemHoverElevation : 0);
        surface.Shadow = isHovered ? new ThemeShadow() : null;
        Canvas.SetZIndex(container, isHovered ? 1 : 0);
    }

    private ListViewItem? FindItemContainer(DependencyObject child)
    {
        for (var current = child;
             current is not null && current != this.ItemList;
             current = VisualTreeHelper.GetParent(current))
        {
            if (current is ListViewItem container)
            {
                return container;
            }
        }

        return null;
    }

    private static IEnumerable<T> FindDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        var childCount = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childCount; index++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, index);
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

    private void OnMoveSelectionUpClick(object sender, RoutedEventArgs args) => this.MoveSelection(-1);

    private void OnMoveSelectionDownClick(object sender, RoutedEventArgs args) => this.MoveSelection(1);

    internal void MoveSelectedItems(int direction) => this.MoveItems(this.GetSelectedItemIds(), direction);

    private void MoveSelection(int direction) => this.MoveItems(this.GetCommandItemIds(), direction);

    private void MoveItems(Guid[] selectedIds, int direction)
    {
        if (this.Stack is not null && this.Stack.MoveItems(selectedIds, direction))
        {
            ShowStatus(
                selectedIds.Length == 1 ? "Moved 1 item." : $"Moved {selectedIds.Length} items.",
                InfoBarSeverity.Success);
        }

        this.UpdateSelectionCommands();
    }

    private void OnSplitSelectionClick(object sender, RoutedEventArgs args)
        => this.SplitItems(this.GetCommandItemIds());

    internal void SplitSelectedItems() => this.SplitItems(this.GetSelectedItemIds());

    private void SplitItems(Guid[] selectedIds)
    {
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
        => await this.DuplicateItemsAsync(this.GetCommandItemIds());

    internal Task DuplicateSelectedItemsAsync() => this.DuplicateItemsAsync(this.GetSelectedItemIds());

    private async Task DuplicateItemsAsync(Guid[] selectedIds)
    {
        var stack = this.Stack;
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

    private async void OnConvertToNoteClick(object sender, RoutedEventArgs args) =>
        await this.ConvertTextToNoteAsync(false);

    private async void OnDuplicateAsNoteClick(object sender, RoutedEventArgs args) =>
        await this.ConvertTextToNoteAsync(true);

    private async Task ConvertTextToNoteAsync(bool duplicate)
    {
        var source = this.Stack;
        var items = this.GetCommandItems();
        var dialogOwner = this.DialogOwner;
        var xamlRoot = this.XamlRoot;
        if (source is null || items is not [{ Kind: DropItemKind.Text } item] || this._isRemovalDialogOpen)
        {
            return;
        }

        this._isRemovalDialogOpen = true;
        this.UpdateSelectionCommands();
        try
        {
            // Capture the command target before closing a hover/context flyout clears it.
            await this.CloseSelectionCommandsFlyoutAsync();
            if (!duplicate)
            {
                var confirmed = dialogOwner is not null
                    ? await StackDialogService.ConfirmConvertTextToNoteAsync(dialogOwner, item)
                    : xamlRoot is not null && await StackDialogService.ConfirmConvertTextToNoteAsync(xamlRoot, item);
                if (!confirmed)
                {
                    return;
                }
            }

            await App.Current.ConvertTextToNoteAsync(source.Model.Id, item.Id, duplicate);
        }
        catch (Exception exception)
        {
            ShowStatus($"The text item could not be converted to a note: {exception.Message}", InfoBarSeverity.Error);
        }
        finally
        {
            this._isRemovalDialogOpen = false;
            this.UpdateSelectionCommands();
        }
    }

    private async void OnOpenSelectionClick(object sender, RoutedEventArgs args)
        => await this.OpenItemsAsync(this.GetCommandItems());

    internal Task OpenSelectedItemAsync() => this.OpenItemsAsync(this.GetSelectedItems());

    private async Task OpenItemsAsync(DropItem[] items)
    {
        var item = items.SingleOrDefault();
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

    private void OnSelectionCommandsFlyoutOpening(object sender, object args)
    {
        // Capture the hovered item's identity before a child menu can dismiss the parent flyout.
        if (this.Stack is { } stack && this.GetCommandItems() is [var item])
        {
            NoteMenu.PopulateItemMenu(this.ItemNotesMenu, stack, item);
        }
    }

    private void OnItemListDoubleTapped(object sender, DoubleTappedRoutedEventArgs args)
    {
        var element = args.OriginalSource as DependencyObject;
        while (element is not null && element != this.ItemList)
        {
            if (element is FrameworkElement { Tag: DropItemViewModel { Model.Note: { } note } })
            {
                args.Handled = true;
                this.SelectionCommandsFlyout.Hide();
                App.Current.ShowNote(note.Id);
                return;
            }

            element = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(element);
        }
    }

    private async void OnOpenContainingFolderClick(object sender, RoutedEventArgs args) =>
        await OpenContainingFolderAsync(this.GetCommandItems().SingleOrDefault());

    internal Task OpenSelectedItemContainerAsync()
    {
        var selected = this.GetSelectedItems();
        return this.OpenContainingFolderAsync(selected is [var item] ? item : null);
    }

    private async Task OpenContainingFolderAsync(DropItem? item)
    {
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
        => CopyItems(this.GetCommandItems());

    internal void CopySelectedItems() => CopyItems(this.GetSelectedItems());

    private static void CopyItems(DropItem[] items)
    {
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
        => CutItems(this.GetCommandItems());

    internal void CutSelectedItems() => CutItems(this.GetSelectedItems());

    private static void CutItems(DropItem[] items)
    {
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
        => await this.DeleteItemsFromDiskAsync(this.GetCommandItems());

    internal Task DeleteSelectedItemsFromDiskAsync() => this.DeleteItemsFromDiskAsync(this.GetSelectedItems());

    private async Task DeleteItemsFromDiskAsync(DropItem[] requestedItems)
    {
        var source = this.Stack;
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
        => await this.RemoveItemsAsync(this.GetCommandItemIds());

    internal Task RemoveSelectedItemsAsync() => this.RemoveItemsAsync(this.GetSelectedItemIds());

    private Task RemoveItemsAsync(Guid[] selectedIds)
    {
        if (this.Stack is not null && selectedIds.Length > 0)
        {
            return this.RequestRemovalAsync(this.Stack, selectedIds);
        }

        return Task.CompletedTask;
    }

    private async void OnItemListKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key == VirtualKey.Escape && this.SelectedItemCount > 0)
        {
            args.Handled = true;
            this.ClearSelection();
            return;
        }

        if (args.Key == VirtualKey.Space && this.GetFocusedItem() is { } focusedItem)
        {
            args.Handled = true;
            if (this.ItemList.SelectedItems.Contains(focusedItem))
            {
                this.ItemList.SelectedItems.Remove(focusedItem);
            }
            else
            {
                this.ItemList.SelectedItems.Add(focusedItem);
            }

            this.UpdateSelectionIndicators();
            return;
        }

        if (args.Key == VirtualKey.Enter && this.GetCommandItems() is [{ Note: { } note }])
        {
            args.Handled = true;
            this.SelectionCommandsFlyout.Hide();
            App.Current.ShowNote(note.Id);
            return;
        }

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

    private DropItemViewModel? GetFocusedItem()
    {
        for (var element = FocusManager.GetFocusedElement(this.XamlRoot) as DependencyObject;
             element is not null && !ReferenceEquals(element, this.ItemList);
             element = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(element))
        {
            if (element is ListViewItem container)
            {
                return this.ItemList.ItemFromContainer(container) as DropItemViewModel;
            }
        }

        return this.ItemList.SelectedItem as DropItemViewModel;
    }

    private async Task RequestRemovalAsync(DropStackViewModel source, IEnumerable<Guid> itemIds)
    {
        if (!source.CanRemoveItems)
        {
            ShowStatus($"Items cannot be removed from {source.Name}.", InfoBarSeverity.Warning);
            return;
        }

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
        this.ItemList.Visibility = isEmpty && !this.OwnsScrolling
            ? Visibility.Collapsed
            : Visibility.Visible;
        this.EmptyMessage.Visibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnStackModelChangedForNotes(object? sender, EventArgs args) => this.UpdateEmptyState();

    private Guid[] GetSelectedItemIds() =>
        this.ItemList.SelectedItems
            .OfType<DropItemViewModel>()
            .Select(static item => item.Model.Id)
            .ToArray();

    private DropItem[] GetSelectedItems()
    {
        var selectedIds = this.GetSelectedItemIds().ToHashSet();
        return this.Stack?.Model.Items
                   .Where(item => selectedIds.Contains(item.Id))
                   .ToArray() ?? [];
    }

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
        if (this._hoveredItemRow is { } hoveredRow)
        {
            this.UpdateItemHoverShadow(hoveredRow, false);
            this.UpdateSelectionIndicator(hoveredRow, false);
        }

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
        this.ConvertToNoteButton.Visibility = this.DuplicateAsNoteButton.Visibility = singleItem?.Kind == DropItemKind.Text
            ? Visibility.Visible : Visibility.Collapsed;
        this.ConvertToNoteButton.IsEnabled = this.DuplicateAsNoteButton.IsEnabled = !this._isRemovalDialogOpen;
        this.ItemNotesButton.Visibility = singleItem is not null && singleItem.Kind != DropItemKind.Note
            ? Visibility.Visible : Visibility.Collapsed;
        this.ItemNotesButton.Label = singleItem?.AttachedNotes.Count > 0
            ? $"Notes ({singleItem.AttachedNotes.Count})" : "Notes";
        this.OpenSelectionButton.Label = singleItem?.Kind == DropItemKind.Note ? "Edit note" : "Open";
        var singleActions = singleItem is null
            ? ContentActions.None
            : ContentMetadataPolicy.GetMetadata(singleItem).Actions;
        this.MoveSelectionUpButton.IsEnabled = hasSelection && this.Stack!.CanMoveItems(selectedIds, -1);
        this.MoveSelectionDownButton.IsEnabled = hasSelection && this.Stack!.CanMoveItems(selectedIds, 1);
        this.SplitSelectionButton.IsEnabled = hasSelection &&
                                              !this.Stack!.IsVirtual &&
                                              selectedIds.Length < this.Stack.Items.Count;
        this.RemoveSelectionButton.IsEnabled = hasSelection &&
                                               this.Stack!.CanRemoveItems &&
                                               !this._isRemovalDialogOpen;
        this.DuplicateSelectionButton.IsEnabled = hasSelection && !this.Stack!.IsVirtual;
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
        this.CutSelectionButton.Visibility = hasSelection &&
                                             this.Stack!.CanRemoveItems &&
                                             selectedItems.All(static item =>
            ContentMetadataPolicy.HasAction(item, ContentActions.Cut))
            ? Visibility.Visible
            : Visibility.Collapsed;
        this.ShowPropertiesButton.Visibility = Has(singleActions, ContentActions.ShowProperties)
            ? Visibility.Visible
            : Visibility.Collapsed;
        this.InspectPayloadButton.Visibility = singleItem is null
            ? Visibility.Collapsed
            : Visibility.Visible;
        this.DeleteFromDiskButton.Visibility = hasSelection &&
                                               this.Stack!.CanRemoveItems &&
                                               selectedItems.All(static item =>
            ContentMetadataPolicy.HasAction(item, ContentActions.Delete))
            ? Visibility.Visible
            : Visibility.Collapsed;
        this.DeleteFromDiskButton.IsEnabled = !this._isRemovalDialogOpen;
        this.SelectionCommandsChanged?.Invoke(this, EventArgs.Empty);
    }

    private static bool Has(ContentActions actions, ContentActions requested) =>
        (actions & requested) == requested;

    private static void ShowStatus(string message, InfoBarSeverity severity) =>
        App.Current.ShowToast(message, severity);
}
