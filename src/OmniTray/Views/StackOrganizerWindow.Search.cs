// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using OmniTray.ViewModels.Organizer;

namespace OmniTray.Views;

public sealed partial class StackOrganizerWindow
{
    private readonly TextBlock _searchPopupEmptyState;
    private readonly FrameworkElement _searchPopupFooter;

    private void OnOrganizerTitleBarSizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (this.GlobalSearchBox is not null)
        {
            this.GlobalSearchBox.Width = Math.Clamp(args.NewSize.Width - 380, 140, 480);
        }
    }

    private void OnSearchAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        this.GlobalSearchBox.Focus(FocusState.Keyboard);
        args.Handled = true;
    }

    private async void OnGlobalSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) { return; }

        this.ClearSearchSuggestions();
        var query = sender.Text.Trim();
        if (query.Length == 0) { return; }

        var groups = await this.ViewModel.Search.FindSuggestionsAsync(query);
        if (groups is null || this._isOrganizerClosed || sender.Text.Trim() != query || !this.IsSearchBoxFocused())
        {
            return;
        }

        this.UpdateSearchPopupFooter(query, groups.Count > 0);
        sender.ItemsSource = groups.Count == 0
            ? null
            : new CollectionViewSource { IsSourceGrouped = true, Source = groups }.View;
        sender.IsSuggestionListOpen = true;
    }

    private bool IsSearchBoxFocused()
    {
        for (var element = FocusManager.GetFocusedElement(this.RootGrid.XamlRoot) as DependencyObject;
             element is not null;
             element = VisualTreeHelper.GetParent(element))
        {
            if (ReferenceEquals(element, this.GlobalSearchBox))
            {
                return true;
            }
        }

        return false;
    }

    private void UpdateSearchPopupFooter(string query, bool hasResults)
    {
        this._searchPopupEmptyState.Text = $"No results for “{query}”";
        this._searchPopupEmptyState.Visibility = hasResults ? Visibility.Collapsed : Visibility.Visible;
        this._searchPopupFooter.Visibility = hasResults ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnShowAllSearchResultsClick(object sender, RoutedEventArgs args) =>
        this.SubmitSearch(this.GlobalSearchBox.Text, null);

    private void OnGlobalSearchQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args) =>
        this.SubmitSearch(args.QueryText, args.ChosenSuggestion as StackSearchResultViewModel);

    private void SubmitSearch(string query, StackSearchResultViewModel? chosenResult)
    {
        if (!this.Navigation.BeginSearch(query, this.ViewModel.Stack.SelectedItem?.Model.Id)) { return; }

        this.ClearSearchSuggestions();
        if (chosenResult is { } result) { this.OpenSearchResult(result); }
        else { this.ShowSearchResults(); }
    }

    private void ShowSearchResults()
    {
        if (this._isOrganizerClosed) { return; }

        this.LeaveNotesPage();
        this.ClearSearchSuggestions();
        this.Navigation.ShowSearch();
        this._stackPage.SetStack(null);
        this.DetailsPane.ShowItem(null, null, 0);
        this.PageHost.Content = null;
        this.BrowserContent.Visibility = Visibility.Collapsed;
        this.SearchHost.Content = this._searchPage;
        this.SearchHost.Visibility = Visibility.Visible;
        this.GlobalSearchBox.Text = this.Navigation.SearchQuery;
        _ = this._searchPage.RefreshAsync(this.Navigation, true);
    }

    private void OnSearchResultOpened(object? sender, StackSearchResultViewModel result) =>
        this.OpenSearchResult(result);

    private void OpenSearchResult(StackSearchResultViewModel result)
    {
        if (result.Note is { } note)
        {
            if (this.ViewModel.Catalog.FindNote(note.Id) is null)
            {
                App.Current.ShowToast("That note is no longer available.", InfoBarSeverity.Warning);
                return;
            }

            App.Current.ShowNote(note.Id);
            return;
        }

        var stack = this.ViewModel.Catalog.Stacks.FirstOrDefault(candidate =>
            candidate.Model.Id == result.Stack.Model.Id);
        var itemId = result.Item?.Model.Id;
        if (stack is null || (itemId is { } id && stack.Items.All(item => item.Model.Id != id)))
        {
            App.Current.ShowToast("That search result is no longer available.", InfoBarSeverity.Warning);
            this.ShowSearchResults();
            return;
        }

        this.Navigation.OpenSearchResult(stack.Model.Id, itemId);
        this.OpenStack(stack, true);
        if (itemId is { } selectedItemId) { this._stackPage.RevealItem(selectedItemId); }
    }

    private void OnSearchBackRequested(object? sender, EventArgs args)
    {
        var origin = this.Navigation.SearchOrigin;
        if (origin?.Page == StackOrganizerPage.Notes)
        {
            this.OrganizerNavigation.SelectedItem = this.NotesNavigationItem;
            this.ShowNotesPage();
            return;
        }

        var stack = this.ViewModel.Catalog.Stacks.FirstOrDefault(candidate => candidate.Model.Id == origin?.StackId);
        if (stack is null)
        {
            this.ShowOverview();
            return;
        }

        this.OpenStack(stack);
        if (origin?.ItemId is { } id) { this._stackPage.RevealItem(id); }
    }

    private void LeaveSearchResults(bool retainSearchNavigation)
    {
        this.ViewModel.Search.CancelResults(!retainSearchNavigation);
        this.ClearSearchSuggestions();
        this.SearchHost.Visibility = Visibility.Collapsed;
        this.SearchHost.Content = null;
        this.BrowserContent.Visibility = Visibility.Visible;
    }

    private void ClearSearchSuggestions()
    {
        this.ViewModel.Search.CancelSuggestions();
        this.GlobalSearchBox.IsSuggestionListOpen = false;
        this.GlobalSearchBox.ItemsSource = null;
        this._searchPopupFooter.Visibility = Visibility.Collapsed;
        this._searchPopupEmptyState.Visibility = Visibility.Collapsed;
    }
}
