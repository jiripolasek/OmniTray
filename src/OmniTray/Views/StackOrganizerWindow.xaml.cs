// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

using System.ComponentModel;
using Windows.ApplicationModel.DataTransfer;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Input;
using OmniTray.Controls;
using OmniTray.Views.Organizer;

namespace OmniTray.Views;

public sealed partial class StackOrganizerWindow : Window
{
    private const double DetailsPaneAvailableWidth = 1080;
    private const double DetailsPaneDefaultWidth = 288;
    private const double DetailsPaneMinimumWidth = 240;
    private const double MainContentMinimumWidth = 360;

    private readonly StackOverviewPage _overviewPage;
    private readonly StackSearchPage _searchPage;
    private readonly StackContentsPage _stackPage;
    private double _detailsPanePreferredWidth = DetailsPaneDefaultWidth;
    private bool _detailsPaneRequestedVisible = true;

    public StackOrganizerViewModel ViewModel { get; }
    private StackOrganizerNavigationState Navigation => this.ViewModel.Navigation;
    private DropStackViewModel? SelectedStack => this.ViewModel.Stack.Stack;
    private NoteEditorPane InlineNoteEditor => this.DetailsPane.NoteEditor;
    private bool IsShowingSearch => this.Navigation.Page == StackOrganizerPage.Search;
    private bool IsShowingNotes => this.Navigation.Page == StackOrganizerPage.Notes;

    public StackOrganizerWindow()
    {
        this.ViewModel = new StackOrganizerViewModel(App.Current.StackCatalogViewModel);
        this._overviewPage = new StackOverviewPage(this.ViewModel.Catalog, this.ViewModel.Overview);
        this._stackPage = new StackContentsPage(this.ViewModel.Stack);
        this._searchPage = new StackSearchPage(this.ViewModel.Search);
        this.InitializeComponent();
        this._searchPopupFooter
            = (FrameworkElement)((DataTemplate)this.RootGrid.Resources["SearchPopupFooterTemplate"]).LoadContent();
        this._searchPopupEmptyState
            = (TextBlock)((DataTemplate)this.RootGrid.Resources["SearchPopupEmptyTemplate"]).LoadContent();
        AutoSuggestBoxGrouping.Enable(this.GlobalSearchBox, this._searchPopupFooter, this._searchPopupEmptyState);

        this.ExtendsContentIntoTitleBar = true;
        this.SetTitleBar(this.AppTitleBar);
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "OmniTray.ico");
        if (File.Exists(iconPath)) { this.AppWindow.SetIcon(iconPath); }

        if (AppWindowTitleBar.IsCustomizationSupported())
        {
            this.AppWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
            this.AppWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        }

        this.InlineNoteEditor.Initialize(this.ViewModel.Catalog);
        this.InlineNoteEditor.SaveStateChanged += this.OnNoteSaveStateChanged;
        this.AppWindow.Closing += this.OnOrganizerClosing;
        this.Activated += this.OnOrganizerActivated;
        this._overviewPage.StackOpened += this.OnStackOpened;
        this._overviewPage.NewStackRequested += this.OnNewStackRequested;
        this._overviewPage.ClipboardStackRequested += this.OnClipboardStackRequested;
        this._overviewPage.DetailsPaneToggleRequested += this.OnDetailsPaneToggleRequested;
        this._stackPage.BackRequested += this.OnStackBackRequested;
        this._stackPage.DetailsPaneToggleRequested += this.OnDetailsPaneToggleRequested;
        this._stackPage.SelectedItemsChanged += this.OnSelectedItemsChanged;
        this._searchPage.BackRequested += this.OnSearchBackRequested;
        this._searchPage.ResultOpened += this.OnSearchResultOpened;
        this.ViewModel.Overview.ScopeCommandCompleted += this.OnCatalogChanged;
        this.ViewModel.Catalog.CatalogChanged += this.OnCatalogChanged;
        this.ViewModel.Catalog.PropertyChanged += this.OnCatalogPropertyChanged;
        this.Closed += this.OnClosed;

        this.OrganizerNavigation.SelectedItem = this.AllStacksNavigationItem;
        this.RefreshVisibleStacks();
        this.ShowOverview();
    }

    internal void SelectStack(DropStackViewModel? stack)
    {
        this.OrganizerNavigation.SelectedItem = this.AllStacksNavigationItem;
        this.Navigation.SelectScope(null);
        this.RefreshVisibleStacks();
        if (stack is not null && this.ViewModel.Catalog.Stacks.Contains(stack)) { this.OpenStack(stack); }
        else { this.ShowOverview(); }

        this.Activate();
    }

    internal void RevealItem(Guid itemId) => this._stackPage.RevealItem(itemId);

    private void OnBrowserContentSizeChanged(object sender, SizeChangedEventArgs args)
    {
        // Clamp layout, not the preferred Width, so widening the window restores the
        // user's last split. Narrow layouts temporarily hide the whole pane.
        this.DetailsColumn.MaxWidth = Math.Max(
            DetailsPaneMinimumWidth,
            args.NewSize.Width - MainContentMinimumWidth - this.DetailsSplitter.Width);
        this.UpdateDetailsPaneLayout(args.NewSize.Width);
    }

    private void OnDetailsPaneToggleRequested(object? sender, EventArgs args)
    {
        this._detailsPaneRequestedVisible = !this._detailsPaneRequestedVisible;
        this.UpdateDetailsPaneLayout(this.BrowserContent.ActualWidth);
    }

    private void UpdateDetailsPaneLayout(double availableWidth)
    {
        var isAvailable = availableWidth >= DetailsPaneAvailableWidth;
        var isCurrentlyVisible = this.DetailsPane.Visibility == Visibility.Visible &&
                                 this.DetailsColumn.ActualWidth >= DetailsPaneMinimumWidth;
        if (isCurrentlyVisible && this.DetailsColumn.Width.IsAbsolute &&
            this.DetailsColumn.Width.Value >= DetailsPaneMinimumWidth)
        {
            this._detailsPanePreferredWidth = this.DetailsColumn.Width.Value;
        }

        var isVisible = isAvailable && this._detailsPaneRequestedVisible;
        if (isVisible)
        {
            this.DetailsColumn.MinWidth = DetailsPaneMinimumWidth;
            this.DetailsColumn.Width = new GridLength(this._detailsPanePreferredWidth);
            this.DetailsSplitter.Visibility = Visibility.Visible;
            this.DetailsPane.Visibility = Visibility.Visible;
        }
        else
        {
            this.DetailsPane.Visibility = Visibility.Collapsed;
            this.DetailsSplitter.Visibility = Visibility.Collapsed;
            this.DetailsColumn.MinWidth = 0;
            this.DetailsColumn.Width = new GridLength(0);
        }

        this._overviewPage.SetDetailsPaneState(isVisible, isAvailable);
        this._stackPage.SetDetailsPaneState(isVisible, isAvailable);
        this._notesPage?.SetDetailsPaneState(isVisible, isAvailable);
    }

    private void OnNewNoteAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        App.Current.CreateQuickNote(this.SelectedStack?.Model.Id ??
                                    (this.Navigation.Page == StackOrganizerPage.Overview
                                        ? this._overviewPage.SelectedStackId
                                        : null));
        args.Handled = true;
    }

    private void OnScopeSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if ((args.SelectedItem as NavigationViewItem)?.Tag is "notes") { this.ShowNotesPage(); }
        else if ((args.SelectedItem as NavigationViewItem)?.Tag is StackOrganizerScopeViewModel scope)
        {
            this.Navigation.SelectScope(scope.Side);
            this.ShowOverview();
            this.RefreshVisibleStacks();
        }
    }

    private async void OnNavigationItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        switch ((args.InvokedItemContainer as NavigationViewItem)?.Tag)
        {
            case StackOrganizerScopeViewModel scope when this.IsShowingSearch || this.Navigation.OpenedFromSearch:
                this.Navigation.SelectScope(scope.Side);
                this.ShowOverview();
                this.RefreshVisibleStacks();
                break;
            case "notes": this.ShowNotesPage(); break;
            case "new-stack": this.CreateNewStack(); break;
            case "new-stack-from-clipboard": await this.CreateNewStackFromClipboardAsync(); break;
            case "settings": App.Current.ShowSettings(); break;
        }
    }

    private void RefreshVisibleStacks()
    {
        this.ViewModel.Overview.ScopeSide = this.Navigation.ScopeSide;
        this._overviewPage.RefreshVisibleStacks();
    }

    private void OnStackOpened(object? sender, DropStackViewModel stack) => this.OpenStack(stack);
    private void OnNewStackRequested(object? sender, EventArgs args) => this.CreateNewStack();

    private async void OnClipboardStackRequested(object? sender, EventArgs args) =>
        await this.CreateNewStackFromClipboardAsync();

    private void OpenStack(DropStackViewModel stack, bool fromSearch = false)
    {
        if (this._isOrganizerClosed || !this.ViewModel.Catalog.Stacks.Contains(stack)) { return; }

        if (this.IsShowingNotes && !fromSearch)
        {
            this.OrganizerNavigation.SelectedItem = this.AllStacksNavigationItem;
        }

        this.LeaveNotesPage();
        this.LeaveSearchResults(fromSearch);
        this.Navigation.OpenStack(stack.Model.Id, fromSearch);
        this.DetailsPane.ShowEmpty("Select an item", "Item details appear here.");
        this.PageHost.Content = this._stackPage;
        this._stackPage.SetStack(stack, fromSearch);
    }

    private void OnStackBackRequested(object? sender, EventArgs args)
    {
        if (this.Navigation.OpenedFromSearch) { this.ShowSearchResults(); }
        else { this.ShowOverview(); }
    }

    private void ShowOverview()
    {
        if (this._isOrganizerClosed) { return; }

        var previousStack = this.SelectedStack;
        this.LeaveNotesPage();
        this.LeaveSearchResults(false);
        this.Navigation.ShowOverview();
        // Clear the outgoing tree before attaching another page; keep the note save session alive.
        this._stackPage.SetStack(null);
        this.DetailsPane.ShowEmpty("Open a stack", "Double-click a stack to organize its items.");
        this.PageHost.Content = this._overviewPage;
        if (previousStack is not null) { this._overviewPage.SelectStack(previousStack); }
    }

    private void OnSelectedItemsChanged(object? sender, EventArgs args) => this.RefreshDetailsPane();

    private void RefreshDetailsPane()
    {
        if (this._isOrganizerClosed) { return; }

        if (this.IsShowingNotes) { this.DetailsPane.ShowNote(this._notesPage?.SelectedNoteId); }
        else
        {
            this.DetailsPane.ShowItem(this.SelectedStack, this.ViewModel.Stack.SelectedItem,
                this.ViewModel.Stack.SelectedItemCount);
        }
    }

    private void CreateNewStack()
    {
        var stack = this.ViewModel.Catalog.AddStack(DropStack.CreateEmpty());
        this.OpenCreatedStack(stack, this.Navigation.ScopeSide);
        _ = this.DispatcherQueue.TryEnqueue(() =>
        {
            if (this.SelectedStack is { } selected)
            {
                _ = StackDialogService.RenameAsync(this.RootGrid.XamlRoot, selected);
            }
        });
    }

    private async Task CreateNewStackFromClipboardAsync()
    {
        var scopeSide = this.Navigation.ScopeSide;
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

            var stack = this.ViewModel.Catalog.AddStack(DropStack.Create(items));
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

    private void OpenCreatedStack(DropStackViewModel stack, EdgeShelfSide? scopeSide)
    {
        if (scopeSide is { } side)
        {
            this.ViewModel.Catalog.AssignStackToEdge(stack, side);
        }

        this.ViewModel.RefreshScopes();
        this.RefreshVisibleStacks();
        this.OpenStack(stack);
    }

    private void OnCatalogChanged(object? sender, EventArgs args)
    {
        if (this._isOrganizerClosed || this.ViewModel.Overview.IsApplyingScopeCommand) { return; }

        this.ViewModel.RefreshScopes();
        this.RefreshVisibleStacks();
        if (this.IsShowingSearch) { _ = this._searchPage.RefreshAsync(this.Navigation); }

        if (this.SelectedStack is { } stack)
        {
            if (this.ViewModel.Catalog.Stacks.Contains(stack))
            {
                this.ViewModel.Stack.Refresh();
                this.RefreshDetailsPane();
            }
            else if (this.Navigation.OpenedFromSearch) { this.ShowSearchResults(); }
            else { this.ShowOverview(); }
        }
    }

    private void OnCatalogPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(MainViewModel.LeftEdgeWindowEnabled) or
            nameof(MainViewModel.RightEdgeWindowEnabled) or nameof(MainViewModel.TopEdgeWindowEnabled) or
            nameof(MainViewModel.BottomEdgeWindowEnabled) or nameof(MainViewModel.SyncLeftAndRightEdgeContent) or
            nameof(MainViewModel.SyncTopAndBottomEdgeContent) or nameof(MainViewModel.SyncAllEdgeContent))
        {
            this.OnCatalogChanged(sender, EventArgs.Empty);
        }
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        this._isOrganizerClosed = true;
        this.AppWindow.Closing -= this.OnOrganizerClosing;
        this.Activated -= this.OnOrganizerActivated;
        this.Closed -= this.OnClosed;
        this.InlineNoteEditor.SaveStateChanged -= this.OnNoteSaveStateChanged;
        this.ViewModel.Catalog.CatalogChanged -= this.OnCatalogChanged;
        this.ViewModel.Catalog.PropertyChanged -= this.OnCatalogPropertyChanged;
        this.ViewModel.Overview.ScopeCommandCompleted -= this.OnCatalogChanged;
        this._overviewPage.StackOpened -= this.OnStackOpened;
        this._overviewPage.NewStackRequested -= this.OnNewStackRequested;
        this._overviewPage.ClipboardStackRequested -= this.OnClipboardStackRequested;
        this._overviewPage.DetailsPaneToggleRequested -= this.OnDetailsPaneToggleRequested;
        this._overviewPage.ClearInsertionAdorner();
        this._stackPage.BackRequested -= this.OnStackBackRequested;
        this._stackPage.DetailsPaneToggleRequested -= this.OnDetailsPaneToggleRequested;
        this._stackPage.SelectedItemsChanged -= this.OnSelectedItemsChanged;
        this._searchPage.BackRequested -= this.OnSearchBackRequested;
        this._searchPage.ResultOpened -= this.OnSearchResultOpened;
        if (this._notesPage is not null)
        {
            this._notesPage.SelectedNoteChanged -= this.OnLibraryNoteSelected;
            this._notesPage.DetailsPaneToggleRequested -= this.OnDetailsPaneToggleRequested;
            this._notesPage.Dispose();
        }

        this.ClearSearchSuggestions();
        this.ViewModel.Search.Dispose();
        this._searchPage.Dispose();
        this._stackPage.Dispose();
        this.DetailsPane.Dispose();
        this.PageHost.Content = null;
        this.SearchHost.Content = null;
    }
}
