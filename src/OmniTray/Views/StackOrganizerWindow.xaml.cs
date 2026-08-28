// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Collections.ObjectModel;
using System.ComponentModel;
using Windows.ApplicationModel.DataTransfer;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using OmniTray.Controls;

namespace OmniTray.Views;

public sealed partial class StackOrganizerWindow : Window
{
    private readonly ListInsertionAdornerController _stackInsertionAdorner;
    private readonly PointerEventHandler _stackPointerMovedHandler;
    private readonly MainViewModel _viewModel = App.Current.StackCatalogViewModel;
    private DropItemViewModel? _detailsItem;
    private DropStackViewModel? _editorStack;
    private EdgeShelfSide? _scopeSide;
    private StackOrganizerSortMode _sortMode;
    private bool _isApplyingScopeCommand;
    private bool _isStackDragOperationActive;
    private bool _isSynchronizingViewButtons;

    public StackOrganizerWindow()
    {
        this._stackPointerMovedHandler = this.OnStackPointerMoved;
        this.Scopes =
        [
            new StackOrganizerScopeViewModel(null, "All stacks", "\uE7B8"),
            new StackOrganizerScopeViewModel(EdgeShelfSide.Left, "Left edge", "\uE76B"),
            new StackOrganizerScopeViewModel(EdgeShelfSide.Right, "Right edge", "\uE76C"),
            new StackOrganizerScopeViewModel(EdgeShelfSide.Top, "Top edge", "\uE70E"),
            new StackOrganizerScopeViewModel(EdgeShelfSide.Bottom, "Bottom edge", "\uE70D")
        ];
        this.InitializeComponent();
        this._searchPopupFooter = (FrameworkElement)((DataTemplate)this.RootGrid.Resources["SearchPopupFooterTemplate"]).LoadContent();
        this._searchPopupEmptyState = (TextBlock)((DataTemplate)this.RootGrid.Resources["SearchPopupEmptyTemplate"]).LoadContent();
        AutoSuggestBoxGrouping.Enable(this.GlobalSearchBox, this._searchPopupFooter, this._searchPopupEmptyState);
        this._stackInsertionAdorner = new ListInsertionAdornerController(
            this.StackGrid,
            "StackInsertionAdorner",
            Orientation.Horizontal);
        this._stackInsertionAdorner.SetLayout(Orientation.Horizontal, true);

        this.ExtendsContentIntoTitleBar = true;
        this.SetTitleBar(this.AppTitleBar);
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "OmniTray.ico");
        if (File.Exists(iconPath))
        {
            this.AppWindow.SetIcon(iconPath);
        }

        if (AppWindowTitleBar.IsCustomizationSupported())
        {
            this.AppWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
            this.AppWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        }

        this.InlineNoteEditor.Initialize(this._viewModel);
        this.InlineNoteEditor.SaveStateChanged += this.OnNoteSaveStateChanged;
        this.AppWindow.Closing += this.OnOrganizerClosing;
        this.Activated += this.OnOrganizerActivated;
        this.ItemsOrganizer.DialogOwner = this;
        this.ItemsOrganizer.SelectedItemsChanged += this.OnSelectedItemsChanged;
        this._viewModel.CatalogChanged += this.OnCatalogChanged;
        this._viewModel.PropertyChanged += this.OnCatalogPropertyChanged;
        this.Closed += this.OnClosed;

        this.OrganizerNavigation.SelectedItem = this.AllStacksNavigationItem;
        this.ApplyOverviewLayout(StackOrganizerLayoutMode.Medium);
        this.RefreshScopes();
        this.RefreshVisibleStacks();
        this.ShowOverview();
    }

    public ObservableCollection<StackOrganizerScopeViewModel> Scopes { get; }

    public StackOrganizerScopeViewModel AllStacksScope => this.Scopes[0];

    public StackOrganizerScopeViewModel LeftEdgeScope => this.Scopes[1];

    public StackOrganizerScopeViewModel RightEdgeScope => this.Scopes[2];

    public StackOrganizerScopeViewModel TopEdgeScope => this.Scopes[3];

    public StackOrganizerScopeViewModel BottomEdgeScope => this.Scopes[4];

    public ObservableCollection<DropStackViewModel> VisibleStacks { get; } = [];

    internal void SelectStack(DropStackViewModel? stack)
    {
        this.OrganizerNavigation.SelectedItem = this.AllStacksNavigationItem;
        this._scopeSide = null;
        this.RefreshVisibleStacks();
        if (stack is not null && this._viewModel.Stacks.Contains(stack))
        {
            this.OpenStack(stack);
        }
        else
        {
            this.ShowOverview();
        }

        this.Activate();
    }

    internal void RevealItem(Guid itemId) => this.ItemsOrganizer.SelectItem(itemId);

    private void OnBrowserContentSizeChanged(object sender, SizeChangedEventArgs args)
    {
        // Clamp layout, not the preferred Width, so widening the window restores the
        // user's last split. The narrow visual states temporarily hide the whole pane.
        this.DetailsColumn.MaxWidth = Math.Max(240, args.NewSize.Width - 360 - 8);
    }

    private void OnNewNoteClick(object sender, RoutedEventArgs args) =>
        App.Current.CreateQuickNote(this._editorStack?.Model.Id ?? (this._isShowingSearch || this._isShowingNotes
            ? null : (this.StackGrid.SelectedItem as DropStackViewModel)?.Model.Id));

    private void OnBrowseNotesClick(object sender, RoutedEventArgs args) => App.Current.ShowNotes();

    private void OnNewNoteAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        this.OnNewNoteClick(this, new RoutedEventArgs());
        args.Handled = true;
    }

    private DropStackViewModel? SelectedStack => this._editorStack;

    private void OnScopeSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if ((args.SelectedItem as NavigationViewItem)?.Tag is "notes")
        {
            this.ShowNotesPage();
            return;
        }
        if ((args.SelectedItem as NavigationViewItem)?.Tag is not StackOrganizerScopeViewModel scope)
        {
            return;
        }

        this._scopeSide = scope.Side;
        this.ShowOverview();
        this.RefreshVisibleStacks();
    }

    private async void OnNavigationItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        switch ((args.InvokedItemContainer as NavigationViewItem)?.Tag)
        {
            case StackOrganizerScopeViewModel scope when this._isShowingSearch || this._openedFromSearch:
                this._scopeSide = scope.Side;
                this.ShowOverview();
                this.RefreshVisibleStacks();
                break;
            case "notes":
                this.ShowNotesPage();
                break;
            case "new-stack":
                this.CreateNewStack();
                break;
            case "new-stack-from-clipboard":
                await this.CreateNewStackFromClipboardAsync();
                break;
            case "settings":
                App.Current.ShowSettings();
                break;
        }
    }

    private void OnStackFilterTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args) =>
        this.RefreshVisibleStacks();

    private void RefreshVisibleStacks()
    {
        var selectedIds = this.StackGrid.SelectedItems
            .OfType<DropStackViewModel>()
            .Select(static stack => stack.Model.Id)
            .ToHashSet();
        IEnumerable<DropStackViewModel> source = this._scopeSide is { } side
            ? this._viewModel.GetEdgeStacks(side)
            : this._viewModel.Stacks;
        var query = this.StackFilterBox.Text.Trim();
        if (query.Length > 0)
        {
            source = source.Where(stack => StackFilter.Matches(stack.Model, query));
        }

        source = this._sortMode switch
        {
            StackOrganizerSortMode.Name => source.OrderBy(
                static stack => stack.Name,
                StringComparer.CurrentCultureIgnoreCase),
            StackOrganizerSortMode.ItemCount => source
                .OrderByDescending(static stack => stack.Model.Items.Count)
                .ThenBy(static stack => stack.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => source
        };

        var visible = source.ToArray();
        if (!this.VisibleStacks.SequenceEqual(visible))
        {
            this.VisibleStacks.Clear();
            foreach (var stack in visible)
            {
                this.VisibleStacks.Add(stack);
            }

            foreach (var stack in visible.Where(stack => selectedIds.Contains(stack.Model.Id)))
            {
                this.StackGrid.SelectedItems.Add(stack);
            }
        }

        this.RefreshOverviewHeader(visible.Length);
        this.OverviewEmptyState.Visibility = visible.Length == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        this.OverviewEmptyDescriptionText.Text = query.Length > 0
            ? "No stacks match this filter."
            : this._scopeSide is { } scopeSide
                ? $"No stacks are assigned to the {scopeSide.GetDisplayName().ToLowerInvariant()} edge. Create one here or move an existing stack to this edge."
                : "Create a stack to start organizing captured content.";
        this.UpdateOverviewSelection();
    }

    private void RefreshOverviewHeader(int visibleCount)
    {
        if (this._scopeSide is not { } side)
        {
            this.OverviewTitleText.Text = "All stacks";
            this.OverviewSummaryText.Text = visibleCount == 1 ? "1 stack" : $"{visibleCount} stacks";
            this.OpenCurrentEdgeButton.Visibility = Visibility.Collapsed;
            return;
        }

        var source = EdgeContentSharingPolicy.ResolveContentSource(
            side,
            this._viewModel.SyncLeftAndRightEdgeContent,
            this._viewModel.SyncTopAndBottomEdgeContent,
            this._viewModel.SyncAllEdgeContent);
        var enabled = this._viewModel.IsEdgeWindowEnabled(side);
        this.OverviewTitleText.Text = $"{side.GetDisplayName()} edge";
        this.OverviewSummaryText.Text = enabled
            ? source == side
                ? visibleCount == 1 ? "1 stack on this edge" : $"{visibleCount} stacks on this edge"
                : $"Shared with the {source.GetDisplayName().ToLowerInvariant()} edge · {visibleCount} {(visibleCount == 1 ? "stack" : "stacks")}"
            : "This edge window is disabled in Settings.";
        this.OpenCurrentEdgeButton.Visibility = Visibility.Visible;
        this.OpenCurrentEdgeButton.IsEnabled = enabled;
    }

    private void RefreshScopes()
    {
        this.Scopes[0].UpdateStatus(
            this._viewModel.Stacks.Count == 1 ? "1 stack" : $"{this._viewModel.Stacks.Count} stacks",
            true);
        foreach (var scope in this.Scopes.Where(static scope => scope.Side is not null))
        {
            var side = scope.Side!.Value;
            var source = EdgeContentSharingPolicy.ResolveContentSource(
                side,
                this._viewModel.SyncLeftAndRightEdgeContent,
                this._viewModel.SyncTopAndBottomEdgeContent,
                this._viewModel.SyncAllEdgeContent);
            var stackCount = this._viewModel.GetEdgeStacks(side).Count;
            var enabled = this._viewModel.IsEdgeWindowEnabled(side);
            var countText = stackCount == 1 ? "1 stack" : $"{stackCount} stacks";
            scope.UpdateStatus(
                enabled
                    ? source == side ? countText : $"Shared · {countText}"
                    : "Disabled",
                enabled);
        }
    }

    private void OnStackGridSelectionChanged(object sender, SelectionChangedEventArgs args) =>
        this.UpdateOverviewSelection();

    private void UpdateOverviewSelection()
    {
        var selectedCount = this.StackGrid.SelectedItems.Count;
        this.OpenSelectedStackButton.IsEnabled = selectedCount == 1;
        this.MoveSelectedStacksButton.IsEnabled = selectedCount > 0;
        this.OverviewSelectionText.Text = selectedCount == 0
            ? this.VisibleStacks.Count == 1 ? "1 stack" : $"{this.VisibleStacks.Count} stacks"
            : selectedCount == 1 ? "1 stack selected" : $"{selectedCount} stacks selected";
    }

    private void OnManualSortClick(object sender, RoutedEventArgs args) =>
        this.ApplyOverviewSort(StackOrganizerSortMode.Manual);

    private void OnNameSortClick(object sender, RoutedEventArgs args) =>
        this.ApplyOverviewSort(StackOrganizerSortMode.Name);

    private void OnItemCountSortClick(object sender, RoutedEventArgs args) =>
        this.ApplyOverviewSort(StackOrganizerSortMode.ItemCount);

    private void ApplyOverviewSort(StackOrganizerSortMode sortMode)
    {
        this._sortMode = sortMode;
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
        this.StackGrid.ItemContainerStyle = (Style)this.RootGrid.Resources[styleKey];
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
        var canReorder = this.CanReorderVisibleStacks;
        var target = canReorder
            ? this._stackInsertionAdorner.Resolve(args.GetPosition(this.StackGrid))
            : null;
        var source = DragDropDataService.ActiveStackReferenceId is { } stackId
            ? this._viewModel.Stacks.FirstOrDefault(stack => stack.Model.Id == stackId)
            : null;
        var canMove = target is not null && (source is null ||
            (this._scopeSide is { } side
                ? this._viewModel.CanMoveStackToEdge(source, side, target.Value.InsertionIndex)
                : this._viewModel.CanMoveStack(source, target.Value.InsertionIndex)));
        if (!canMove)
        {
            this._stackInsertionAdorner.Clear();
            args.AcceptedOperation = DataPackageOperation.None;
            args.DragUIOverride.Caption = !string.IsNullOrWhiteSpace(this.StackFilterBox.Text)
                ? "Clear the filter to reorder stacks"
                : this._sortMode != StackOrganizerSortMode.Manual
                    ? "Choose Manual order to reorder stacks"
                    : "Stack is already in this position";
        }
        else
        {
            this._stackInsertionAdorner.Show(target!.Value);
            args.AcceptedOperation = DragDropDataService.GetAcceptedInternalMoveOperation(args.DataView);
            args.DragUIOverride.Caption = this._scopeSide is { } scopeSide
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
        var target = this.CanReorderVisibleStacks
            ? this._stackInsertionAdorner.Resolve(args.GetPosition(this.StackGrid))
            : null;
        this._stackInsertionAdorner.Clear();
        if (target is null)
        {
            return;
        }

        var stackId = await DragDropDataService.ReadStackReferenceAsync(args.DataView);
        var stack = stackId is { } id
            ? this._viewModel.Stacks.FirstOrDefault(candidate => candidate.Model.Id == id)
            : null;
        if (stack is null)
        {
            App.Current.ShowToast("That stack is no longer available.", InfoBarSeverity.Warning);
            return;
        }

        var moved = this._scopeSide is { } side
            ? this._viewModel.MoveStackToEdge(stack, side, target.Value.InsertionIndex)
            : this._viewModel.MoveStack(stack, target.Value.InsertionIndex);
        if (moved)
        {
            this.RefreshVisibleStacks();
        }
    }

    private void OnEdgeNavigationDragOver(object sender, DragEventArgs args)
    {
        args.Handled = true;
        this._stackInsertionAdorner.Clear();
        args.AcceptedOperation = DataPackageOperation.None;
        var caption = "Drop a stack here to move it to this edge";
        if (sender is NavigationViewItem { Tag: StackOrganizerScopeViewModel { Side: { } side } } &&
            DragDropDataService.HasStackReference(args.DataView))
        {
            var source = DragDropDataService.ActiveStackReferenceId is { } stackId
                ? this._viewModel.Stacks.FirstOrDefault(stack => stack.Model.Id == stackId)
                : null;
            if (source is not null && this._viewModel.GetEdgeStacks(side).Contains(source))
            {
                // GetEdgeStacks resolves shared edges, so this also rejects moves within a shared collection.
                caption = "Stack is already on this edge";
            }
            else
            {
                args.AcceptedOperation = DragDropDataService.GetAcceptedInternalMoveOperation(args.DataView);
                caption = $"Move to the {side.GetDisplayName().ToLowerInvariant()} edge";
                if (!this._viewModel.IsEdgeWindowEnabled(side))
                {
                    caption += " (edge window is disabled)";
                }
            }
        }

        args.DragUIOverride.Caption = caption;
        args.DragUIOverride.IsCaptionVisible = true;
        args.DragUIOverride.IsContentVisible = true;
        args.DragUIOverride.IsGlyphVisible = true;
    }

    private async void OnEdgeNavigationDrop(object sender, DragEventArgs args)
    {
        args.Handled = true;
        this._stackInsertionAdorner.Clear();
        args.AcceptedOperation = DataPackageOperation.None;
        if (sender is not NavigationViewItem { Tag: StackOrganizerScopeViewModel { Side: { } side } } ||
            !DragDropDataService.HasStackReference(args.DataView))
        {
            return;
        }

        var deferral = args.GetDeferral();
        try
        {
            // Reading the private reference marks the drop as internal, avoiding external-move cleanup.
            var stackId = await DragDropDataService.ReadStackReferenceAsync(args.DataView);
            var stack = stackId is { } id
                ? this._viewModel.Stacks.FirstOrDefault(candidate => candidate.Model.Id == id)
                : null;
            if (stack is null)
            {
                App.Current.ShowToast("That stack is no longer available.", InfoBarSeverity.Warning);
                return;
            }

            if (this._viewModel.AssignStackToEdge(stack, side))
            {
                args.AcceptedOperation = DragDropDataService.GetAcceptedInternalMoveOperation(args.DataView);
                App.Current.ShowToast(
                    $"Moved {stack.Name} to the {side.GetDisplayName().ToLowerInvariant()} edge.",
                    InfoBarSeverity.Success);
            }
        }
        catch (Exception exception)
        {
            App.Current.ShowToast($"The stack could not be moved: {exception.Message}", InfoBarSeverity.Error);
        }
        finally
        {
            deferral.Complete();
        }
    }

    private bool CanReorderVisibleStacks =>
        this._sortMode == StackOrganizerSortMode.Manual &&
        string.IsNullOrWhiteSpace(this.StackFilterBox.Text);

    private static DropStackViewModel? GetTaggedStack(object sender) =>
        (sender as FrameworkElement)?.Tag as DropStackViewModel;

    private void OnOpenTaggedStackClick(object sender, RoutedEventArgs args)
    {
        if ((sender as FrameworkElement)?.Tag is DropStackViewModel stack)
        {
            this.OpenStack(stack);
        }
    }

    private void OpenStack(DropStackViewModel stack, bool fromSearch = false)
    {
        if (!this._viewModel.Stacks.Contains(stack))
        {
            return;
        }

        if (this._isShowingNotes && !fromSearch)
        {
            this.OrganizerNavigation.SelectedItem = this.AllStacksNavigationItem;
        }
        this.LeaveNotesPage();
        this.LeaveSearchResults(fromSearch);
        this.UpdateDetailsItem(null);
        this._editorStack = stack;
        this.OverviewContent.Visibility = Visibility.Collapsed;
        this.StackContent.Visibility = Visibility.Visible;
        this.DetailsEmptyTitleText.Text = "Select an item";
        this.DetailsEmptyDescriptionText.Text = "Item details appear here.";
        this.ItemsOrganizer.Stack = stack;
        NoteMenu.SetStack(this.EditorNotesMenu, stack);
        this.UpdateStackHeader(stack);
        this.ApplyStackViewMode(stack.InspectorViewMode);
        this.UpdateSelectionSummary();
    }

    private void OnBackToOverviewClick(object sender, RoutedEventArgs args)
    {
        if (this._openedFromSearch)
        {
            this.ShowSearchResults();
        }
        else
        {
            this.ShowOverview();
        }
    }

    private void ShowOverview()
    {
        this.LeaveNotesPage();
        this.LeaveSearchResults(false);
        var previousStack = this._editorStack;
        this._editorStack = null;
        this.UpdateDetailsItem(null);
        this.ItemsOrganizer.Stack = null;
        NoteMenu.SetStack(this.EditorNotesMenu, null);
        this.StackContent.Visibility = Visibility.Collapsed;
        this.OverviewContent.Visibility = Visibility.Visible;
        this.DetailsEmptyTitleText.Text = "Open a stack";
        this.DetailsEmptyDescriptionText.Text = "Double-click a stack to organize its items.";
        if (previousStack is not null && this.VisibleStacks.Contains(previousStack))
        {
            this.StackGrid.SelectedItems.Clear();
            this.StackGrid.SelectedItem = previousStack;
            this.StackGrid.ScrollIntoView(previousStack);
        }
    }

    private void UpdateStackHeader(DropStackViewModel stack)
    {
        this.StackTitleText.Text = stack.Name;
        this.StackSummaryText.Text = $"{stack.ItemCountText} · {stack.EdgePlacementText}";
        this.DetailsStackText.Text = stack.Name;
    }

    private void ApplyStackViewMode(StackInspectorViewMode viewMode)
    {
        this._isSynchronizingViewButtons = true;
        try
        {
            this.StackListViewItem.IsChecked = viewMode == StackInspectorViewMode.List;
            this.StackGridViewItem.IsChecked = viewMode == StackInspectorViewMode.Grid;
            this.StackViewIcon.Glyph = viewMode == StackInspectorViewMode.Grid ? "\uE8A9" : "\uEA37";
            ToolTipService.SetToolTip(
                this.StackViewButton,
                viewMode == StackInspectorViewMode.Grid ? "Item layout: Grid" : "Item layout: List");
            this.ItemsOrganizer.SetThumbnailView(viewMode == StackInspectorViewMode.Grid);
        }
        finally
        {
            this._isSynchronizingViewButtons = false;
        }
    }

    private void OnListViewClick(object sender, RoutedEventArgs args) =>
        this.ChangeStackViewMode(StackInspectorViewMode.List);

    private void OnThumbnailViewClick(object sender, RoutedEventArgs args) =>
        this.ChangeStackViewMode(StackInspectorViewMode.Grid);

    private void ChangeStackViewMode(StackInspectorViewMode viewMode)
    {
        if (this._isSynchronizingViewButtons || this.SelectedStack is not { } stack)
        {
            return;
        }

        if (stack.InspectorViewMode != viewMode)
        {
            stack.ChangeInspectorViewMode(viewMode);
        }

        this.ApplyStackViewMode(viewMode);
    }

    private void OnSelectedItemsChanged(object? sender, EventArgs args)
    {
        this.UpdateSelectionSummary();
        this.UpdateDetailsItem(this.ItemsOrganizer.PrimarySelectedItem);
    }

    private void UpdateSelectionSummary()
    {
        var selectedCount = this.ItemsOrganizer.SelectedItemCount;
        this.SelectionSummaryText.Text = this.SelectedStack is not { } stack
            ? string.Empty
            : selectedCount == 0
                ? stack.ItemCountText
                : selectedCount == 1 ? "1 item selected" : $"{selectedCount} items selected";
    }

    private void UpdateDetailsItem(DropItemViewModel? item)
    {
        if (ReferenceEquals(this._detailsItem, item))
        {
            this.RefreshDetailsPane();
            return;
        }

        if (this._detailsItem is not null)
        {
            this._detailsItem.PropertyChanged -= this.OnDetailsItemPropertyChanged;
        }

        this._detailsItem = item;
        if (item is not null)
        {
            item.PropertyChanged += this.OnDetailsItemPropertyChanged;
        }

        this.RefreshDetailsPane();
    }

    private void OnDetailsItemPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(DropItemViewModel.ThumbnailSource) or
            nameof(DropItemViewModel.ThumbnailIsShellIcon) or
            nameof(DropItemViewModel.ThumbnailHasVideoFilmstrip) or
            nameof(DropItemViewModel.LeadingGlyph))
        {
            this.RefreshDetailsPane();
        }
    }

    private void RefreshDetailsPane()
    {
        if (this._isOrganizerClosed) { return; }
        var item = this._detailsItem;
        var noteId = this._isShowingNotes ? this._notesPage?.SelectedNoteId
            : this.ItemsOrganizer.SelectedItemCount == 1 ? item?.Model.Note?.Id : null;
        this.InlineNoteEditor.SetNote(noteId);
        this.InlineNoteEditor.Visibility = this.InlineNoteEditor.NoteId is null ? Visibility.Collapsed : Visibility.Visible;
        if (this.InlineNoteEditor.NoteId is not null)
        {
            this.DetailsEmptyState.Visibility = Visibility.Collapsed;
            this.DetailsScrollViewer.Visibility = Visibility.Collapsed;
            this.DetailsThumbnail.Source = null;
            return;
        }
        this.DetailsEmptyState.Visibility = item is null ? Visibility.Visible : Visibility.Collapsed;
        this.DetailsScrollViewer.Visibility = item is null ? Visibility.Collapsed : Visibility.Visible;
        if (item is null)
        {
            this.DetailsThumbnail.Source = null;
            return;
        }

        var model = item.Model;
        this.DetailsThumbnail.Source = item.ThumbnailSource;
        this.DetailsThumbnail.IsShellIcon = item.ThumbnailIsShellIcon;
        this.DetailsThumbnail.HasVideoFilmstrip = item.ThumbnailHasVideoFilmstrip;
        this.DetailsPlaceholderIcon.Glyph = item.LeadingGlyph;
        this.DetailsPlaceholderIcon.Visibility = item.ThumbnailSource is null
            ? Visibility.Visible
            : Visibility.Collapsed;
        this.DetailsNameText.Text = item.DisplayName;
        this.DetailsKindText.Text = item.KindLabel;
        this.DetailsAddedText.Text = model.CreatedAt.LocalDateTime.ToString("g");
        this.DetailsStackText.Text = this.SelectedStack?.Name ?? string.Empty;
        this.DetailsSizeSection.Visibility = model.FileFacts?.Size is not null
            ? Visibility.Visible
            : Visibility.Collapsed;
        this.DetailsSizeText.Text = model.FileFacts?.Size is { } byteCount
            ? DataFormatInspectionText.FormatByteCount(byteCount)
            : string.Empty;

        var (label, location) = GetLocation(model);
        this.DetailsLocationLabel.Text = label;
        this.DetailsLocationText.Text = location;
    }

    private static (string Label, string Value) GetLocation(DropItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.SourcePath))
        {
            return ("Path", item.SourcePath);
        }

        if (!string.IsNullOrWhiteSpace(item.Url))
        {
            return ("URL", item.Url);
        }

        if (!string.IsNullOrWhiteSpace(item.SourceUrl))
        {
            return ("Source", item.SourceUrl);
        }

        if (!string.IsNullOrWhiteSpace(item.ApplicationLink))
        {
            return ("Application link", item.ApplicationLink);
        }

        if (!string.IsNullOrWhiteSpace(item.Text))
        {
            return ("Preview", DataFormatInspectionText.CreatePreview(item.Text, 240));
        }

        return ("Storage", "Stored in OmniTray");
    }

    private async void OnPasteIntoStackClick(object sender, RoutedEventArgs args)
    {
        if (this.SelectedStack is { } stack)
        {
            await App.Current.InsertClipboardContentAsync(stack);
        }
    }

    private void OnPopOutSelectedStackClick(object sender, RoutedEventArgs args)
    {
        if (this.SelectedStack is { } stack)
        {
            App.Current.OpenTray(stack);
        }
    }

    private void OnOpenStackInTrayClick(object sender, RoutedEventArgs args)
    {
        if ((sender as FrameworkElement)?.Tag is DropStackViewModel stack)
        {
            App.Current.OpenTray(stack);
        }
    }

    private async void OnRenameSelectedStackClick(object sender, RoutedEventArgs args)
    {
        if (this.SelectedStack is { } stack)
        {
            await StackDialogService.RenameAsync(this.RootGrid.XamlRoot, stack);
        }
    }

    private void OnRenameTitlePointerChanged(object sender, PointerRoutedEventArgs args) =>
        this.UpdateRenameEditIconVisibility();

    private void OnRenameTitleFocusChanged(object sender, RoutedEventArgs args) =>
        this.UpdateRenameEditIconVisibility();

    private void UpdateRenameEditIconVisibility() =>
        this.RenameEditIcon.Opacity = this.RenameTitleButton.IsPointerOver ||
                                      this.RenameTitleButton.FocusState != FocusState.Unfocused
            ? 1
            : 0;

    private async void OnRenameTaggedStackClick(object sender, RoutedEventArgs args)
    {
        if ((sender as FrameworkElement)?.Tag is DropStackViewModel stack)
        {
            await StackDialogService.RenameAsync(this.RootGrid.XamlRoot, stack);
        }
    }

    private async void OnDeleteSelectedStackClick(object sender, RoutedEventArgs args)
    {
        if (this.SelectedStack is { } stack)
        {
            await this.DeleteStackAsync(stack);
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

    private void OnNewStackClick(object sender, RoutedEventArgs args)
        => this.CreateNewStack();

    private void CreateNewStack()
    {
        var stack = this._viewModel.AddStack(DropStack.CreateEmpty());
        this.OpenCreatedStack(stack, this._scopeSide);
        _ = this.DispatcherQueue.TryEnqueue(() =>
        {
            if (this.SelectedStack is { } selected)
            {
                _ = StackDialogService.RenameAsync(this.RootGrid.XamlRoot, selected);
            }
        });
    }

    private async void OnNewStackFromClipboardClick(object sender, RoutedEventArgs args)
        => await this.CreateNewStackFromClipboardAsync();

    private async Task CreateNewStackFromClipboardAsync()
    {
        var scopeSide = this._scopeSide;
        try
        {
            var items = await DragDropDataService.ReadAsync(
                Clipboard.GetContent(),
                CaptureChannel.Clipboard);
            if (items.Count == 0)
            {
                App.Current.ShowToast(
                    "The clipboard does not contain files, folders, text, rich content, an image, or a URL.",
                    InfoBarSeverity.Warning);
                return;
            }

            var stack = this._viewModel.AddStack(DropStack.Create(items));
            this.OpenCreatedStack(stack, scopeSide);
            App.Current.ShowToast(
                items.Count == 1
                    ? "Created a new stack with 1 clipboard item."
                    : $"Created a new stack with {items.Count} clipboard items.",
                InfoBarSeverity.Success);
        }
        catch (Exception exception)
        {
            App.Current.ShowToast(
                $"The clipboard content could not be captured: {exception.Message}",
                InfoBarSeverity.Error);
        }
    }

    private void OnNewStackNavigationDragOver(object sender, DragEventArgs args)
    {
        args.Handled = true;
        this._stackInsertionAdorner.Clear();
        args.AcceptedOperation = DataPackageOperation.None;
        var caption = "Drop items here to create a new stack";
        if (!DragDropDataService.HasStackReference(args.DataView) &&
            DragDropDataService.HasSupportedFormat(args.DataView))
        {
            var isItemTransfer = DragDropDataService.HasItemReference(args.DataView);
            var copy = !isItemTransfer || (args.Modifiers & DragDropModifiers.Control) != 0;
            args.AcceptedOperation = copy
                ? DataPackageOperation.Copy
                : DragDropDataService.GetAcceptedInternalMoveOperation(args.DataView);
            caption = this._scopeSide is { } side
                ? $"Create a new stack on the {side.GetDisplayName().ToLowerInvariant()} edge"
                : "Create a new stack";
            if (isItemTransfer)
            {
                caption = $"{(copy ? "Copy" : "Move")} items — {caption}";
            }
        }

        args.DragUIOverride.Caption = caption;
        args.DragUIOverride.IsCaptionVisible = true;
        args.DragUIOverride.IsContentVisible = true;
        args.DragUIOverride.IsGlyphVisible = true;
    }

    private async void OnNewStackNavigationDrop(object sender, DragEventArgs args)
    {
        args.Handled = true;
        this._stackInsertionAdorner.Clear();
        args.AcceptedOperation = DataPackageOperation.None;
        if (DragDropDataService.HasStackReference(args.DataView) ||
            !DragDropDataService.HasSupportedFormat(args.DataView))
        {
            return;
        }

        var scopeSide = this._scopeSide;
        var copy = (args.Modifiers & DragDropModifiers.Control) != 0;
        var deferral = args.GetDeferral();
        try
        {
            DropStackViewModel stack;
            if (DragDropDataService.HasItemReference(args.DataView))
            {
                var created = await this.CreateStackFromItemDropAsync(args.DataView, copy);
                if (created is null)
                {
                    return;
                }

                stack = created;
                args.AcceptedOperation = copy
                    ? DataPackageOperation.Copy
                    : DragDropDataService.GetAcceptedInternalMoveOperation(args.DataView);
            }
            else
            {
                var items = await DragDropDataService.ReadAsync(args.DataView);
                if (items.Count == 0)
                {
                    App.Current.ShowToast("This drag did not contain a supported payload.", InfoBarSeverity.Warning);
                    return;
                }

                stack = this._viewModel.AddStack(DropStack.Create(items));
                args.AcceptedOperation = DataPackageOperation.Copy;
            }

            this.OpenCreatedStack(stack, scopeSide);
            App.Current.ShowToast(
                $"Created a new stack with {stack.Model.Items.Count} {(stack.Model.Items.Count == 1 ? "item" : "items")}.",
                InfoBarSeverity.Success);
        }
        catch (Exception exception)
        {
            App.Current.ShowToast($"The stack could not be created: {exception.Message}", InfoBarSeverity.Error);
        }
        finally
        {
            deferral.Complete();
        }
    }

    private async Task<DropStackViewModel?> CreateStackFromItemDropAsync(DataPackageView dataView, bool copy)
    {
        // Resolve private item identity before public formats, and suppress external-move cleanup.
        var reference = await DragDropDataService.ReadItemReferenceAsync(dataView);
        var source = reference is null
            ? null
            : this._viewModel.Stacks.FirstOrDefault(stack => stack.Model.Id == reference.SourceStackId);
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
        var created = this._viewModel.AddStack(DropStack.CreateEmpty(name, source.Tint));
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
                this._viewModel.RemoveStack(created);
            }
        }
    }

    private void OpenCreatedStack(DropStackViewModel stack, EdgeShelfSide? scopeSide)
    {
        if (scopeSide is { } side)
        {
            this._viewModel.AssignStackToEdge(stack, side);
        }

        this.RefreshScopes();
        this.RefreshVisibleStacks();
        this.OpenStack(stack);
    }

    private void OnOpenCurrentEdgeShelfClick(object sender, RoutedEventArgs args)
    {
        if (this._scopeSide is { } side)
        {
            App.Current.ShowEdgeShelf(side);
        }
    }

    private DropStackViewModel[] GetSelectedOverviewStacks() =>
        this.StackGrid.SelectedItems.OfType<DropStackViewModel>().ToArray();

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
        var stacks = this.GetSelectedOverviewStacks();
        if (stacks.Length == 0)
        {
            return;
        }

        this._isApplyingScopeCommand = true;
        var changedCount = 0;
        try
        {
            foreach (var stack in stacks)
            {
                if (this._viewModel.AssignStackToEdge(stack, side))
                {
                    changedCount++;
                }
            }
        }
        finally
        {
            this._isApplyingScopeCommand = false;
        }

        if (changedCount > 0)
        {
            App.Current.ShowToast(
                $"Moved {changedCount} {(changedCount == 1 ? "stack" : "stacks")} to the {side.GetDisplayName().ToLowerInvariant()} edge.",
                InfoBarSeverity.Success);
        }
        this.RefreshScopes();
        this.RefreshVisibleStacks();
    }

    private void OnRemoveSelectedFromEdgesClick(object sender, RoutedEventArgs args)
    {
        var stacks = this.GetSelectedOverviewStacks();
        if (stacks.Length == 0)
        {
            return;
        }

        this._isApplyingScopeCommand = true;
        var changedCount = 0;
        try
        {
            foreach (var stack in stacks)
            {
                if (this._viewModel.RemoveStackFromEdge(stack))
                {
                    changedCount++;
                }
            }
        }
        finally
        {
            this._isApplyingScopeCommand = false;
        }

        if (changedCount > 0)
        {
            App.Current.ShowToast(
                $"Removed {changedCount} {(changedCount == 1 ? "stack" : "stacks")} from the edge shelves.",
                InfoBarSeverity.Success);
        }
        this.RefreshScopes();
        this.RefreshVisibleStacks();
    }

    private void OnEdgeAssignmentFlyoutOpening(object sender, object args)
    {
        var assignedEdge = this.SelectedStack?.AssignedEdge;
        this.NoEdgeAssignmentItem.IsChecked = assignedEdge is null;
        this.LeftEdgeAssignmentItem.IsChecked = assignedEdge == EdgeShelfSide.Left;
        this.RightEdgeAssignmentItem.IsChecked = assignedEdge == EdgeShelfSide.Right;
        this.TopEdgeAssignmentItem.IsChecked = assignedEdge == EdgeShelfSide.Top;
        this.BottomEdgeAssignmentItem.IsChecked = assignedEdge == EdgeShelfSide.Bottom;
    }

    private void OnAssignLeftEdgeClick(object sender, RoutedEventArgs args) =>
        this.AssignEditorStackToEdge(EdgeShelfSide.Left);

    private void OnAssignRightEdgeClick(object sender, RoutedEventArgs args) =>
        this.AssignEditorStackToEdge(EdgeShelfSide.Right);

    private void OnAssignTopEdgeClick(object sender, RoutedEventArgs args) =>
        this.AssignEditorStackToEdge(EdgeShelfSide.Top);

    private void OnAssignBottomEdgeClick(object sender, RoutedEventArgs args) =>
        this.AssignEditorStackToEdge(EdgeShelfSide.Bottom);

    private void AssignEditorStackToEdge(EdgeShelfSide side)
    {
        if (this.SelectedStack is not { } stack || !this._viewModel.AssignStackToEdge(stack, side))
        {
            return;
        }

        App.Current.ShowToast(
            $"Placed {stack.Name} on the {side.GetDisplayName().ToLowerInvariant()} edge.",
            InfoBarSeverity.Success);
        this.RefreshScopes();
        this.UpdateStackHeader(stack);
    }

    private void OnRemoveEdgeAssignmentClick(object sender, RoutedEventArgs args)
    {
        if (this.SelectedStack is not { } stack || !this._viewModel.RemoveStackFromEdge(stack))
        {
            return;
        }

        App.Current.ShowToast($"Removed {stack.Name} from the edge shelves.", InfoBarSeverity.Success);
        this.RefreshScopes();
        this.UpdateStackHeader(stack);
    }

    private void OnCatalogChanged(object? sender, EventArgs args)
    {
        if (this._isApplyingScopeCommand)
        {
            return;
        }

        this.RefreshScopes();
        this.RefreshVisibleStacks();
        if (this._isShowingSearch)
        {
            _ = this.RefreshSearchResultsAsync();
        }

        if (this.SelectedStack is { } stack)
        {
            if (this._viewModel.Stacks.Contains(stack))
            {
                this.UpdateStackHeader(stack);
            }
            else
            {
                if (this._openedFromSearch)
                {
                    this.ShowSearchResults();
                }
                else
                {
                    this.ShowOverview();
                }
            }
        }
    }

    private void OnCatalogPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(MainViewModel.LeftEdgeWindowEnabled) or
            nameof(MainViewModel.RightEdgeWindowEnabled) or
            nameof(MainViewModel.TopEdgeWindowEnabled) or
            nameof(MainViewModel.BottomEdgeWindowEnabled) or
            nameof(MainViewModel.SyncLeftAndRightEdgeContent) or
            nameof(MainViewModel.SyncTopAndBottomEdgeContent) or
            nameof(MainViewModel.SyncAllEdgeContent))
        {
            this.RefreshScopes();
            this.RefreshVisibleStacks();
            if (this._isShowingSearch)
            {
                _ = this.RefreshSearchResultsAsync();
            }
        }
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        this._isOrganizerClosed = true;
        this.AppWindow.Closing -= this.OnOrganizerClosing;
        this.Activated -= this.OnOrganizerActivated;
        this.InlineNoteEditor.SaveStateChanged -= this.OnNoteSaveStateChanged;
        this.InlineNoteEditor.Dispose();
        if (this._notesPage is not null) { this._notesPage.SelectedNoteChanged -= this.OnLibraryNoteSelected; }
        this.LeaveNotesPage();
        this.CancelSearchRequests();
        this.Closed -= this.OnClosed;
        this.ItemsOrganizer.SelectedItemsChanged -= this.OnSelectedItemsChanged;
        this._viewModel.CatalogChanged -= this.OnCatalogChanged;
        this._viewModel.PropertyChanged -= this.OnCatalogPropertyChanged;
        this.UpdateDetailsItem(null);
    }

    private enum StackOrganizerSortMode
    {
        Manual,
        Name,
        ItemCount
    }

    private enum StackOrganizerLayoutMode
    {
        Compact,
        Medium,
        Large
    }
}

public sealed class StackOrganizerScopeViewModel : ObservableObject
{
    private bool _canOpen;
    private string _statusText = string.Empty;

    public StackOrganizerScopeViewModel(EdgeShelfSide? side, string displayName, string glyph)
    {
        this.Side = side;
        this.DisplayName = displayName;
        this.Glyph = glyph;
    }

    public EdgeShelfSide? Side { get; }

    public string DisplayName { get; }

    public string Glyph { get; }

    public string StatusText
    {
        get => this._statusText;
        private set => this.SetProperty(ref this._statusText, value);
    }

    public bool CanOpen
    {
        get => this._canOpen;
        private set => this.SetProperty(ref this._canOpen, value);
    }

    public Visibility OpenButtonVisibility => this.Side is null ? Visibility.Collapsed : Visibility.Visible;

    public string OpenButtonAccessibleName => this.Side is { } side
        ? $"Open {side.GetDisplayName().ToLowerInvariant()} edge shelf"
        : string.Empty;

    internal void UpdateStatus(string statusText, bool canOpen)
    {
        this.StatusText = statusText;
        this.CanOpen = canOpen;
    }
}
