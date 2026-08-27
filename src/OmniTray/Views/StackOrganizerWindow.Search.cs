// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Collections.ObjectModel;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace OmniTray.Views;

public sealed partial class StackOrganizerWindow
{
    private readonly FrameworkElement _searchPopupFooter;
    private readonly TextBlock _searchPopupEmptyState;
    private CancellationTokenSource? _searchCancellation;
    private CancellationTokenSource? _suggestionCancellation;
    private string _searchQuery = string.Empty;
    private bool _isShowingSearch;
    private bool _openedFromSearch;
    private Guid? _searchOriginStackId;
    private Guid? _searchOriginItemId;
    private Guid? _lastSearchStackId;
    private Guid? _lastSearchItemId;

    public ObservableCollection<StackSearchResultViewModel> SearchResults { get; } = [];

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
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
        {
            return;
        }

        this.ClearSearchSuggestions();
        var query = sender.Text.Trim();
        if (query.Length == 0)
        {
            return;
        }

        var cancellation = new CancellationTokenSource();
        this._suggestionCancellation = cancellation;
        var token = cancellation.Token;
        try
        {
            await Task.Delay(150, token);
            var snapshot = this._viewModel.Stacks.Select(stack => stack.Model).ToArray();
            var matches = await Task.Run(() => StackCatalogSearch.Find(snapshot, query, token), token);
            if (token.IsCancellationRequested || !ReferenceEquals(this._suggestionCancellation, cancellation) ||
                sender.Text.Trim() != query || !this.IsSearchBoxFocused())
            {
                return;
            }

            var groups = StackSearchResultGroup.Create(this.CreateSearchResults(matches), stackLimit: 3, itemLimit: 4);
            this.UpdateSearchPopupFooter(query, groups.Count > 0);
            sender.ItemsSource = groups.Count == 0
                ? null
                : new CollectionViewSource { IsSourceGrouped = true, Source = groups }.View;
            sender.IsSuggestionListOpen = true;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            // Suggestions are optional; submitting the query still reports any search error on the results page.
            if (ReferenceEquals(this._suggestionCancellation, cancellation))
            {
                sender.IsSuggestionListOpen = false;
            }
        }
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
        query = query.Trim();
        if (query.Length == 0)
        {
            return;
        }

        if (!this._isShowingSearch && !this._openedFromSearch)
        {
            this._searchOriginStackId = this._editorStack?.Model.Id;
            this._searchOriginItemId = this.ItemsOrganizer.PrimarySelectedItem?.Model.Id;
        }

        this._searchQuery = query;
        this._lastSearchStackId = null;
        this._lastSearchItemId = null;
        this.ClearSearchSuggestions();
        if (chosenResult is { } result)
        {
            this.OpenSearchResult(result);
        }
        else
        {
            this.ShowSearchResults();
        }
    }

    private void ShowSearchResults()
    {
        this.ClearSearchSuggestions();
        this._isShowingSearch = true;
        this._openedFromSearch = false;
        this._editorStack = null;
        this.UpdateDetailsItem(null);
        this.ItemsOrganizer.Stack = null;
        this.BrowserContent.Visibility = Visibility.Collapsed;
        this.SearchContent.Visibility = Visibility.Visible;
        this.GlobalSearchBox.Text = this._searchQuery;
        _ = this.RefreshSearchResultsAsync(focusResults: true);
    }

    private async Task RefreshSearchResultsAsync(bool focusResults = false)
    {
        CancelRequest(ref this._searchCancellation);
        var cancellation = new CancellationTokenSource();
        this._searchCancellation = cancellation;
        var token = cancellation.Token;
        var query = this._searchQuery;
        this.SearchResults.Clear();
        this.SearchResultsList.ItemsSource = null;
        this.SearchEmptyState.Visibility = Visibility.Collapsed;
        this.SearchProgressRing.Visibility = Visibility.Visible;
        this.SearchProgressRing.IsActive = true;
        this.SearchSummaryText.Text = $"Searching all stacks and items for “{query}”…";
        try
        {
            var snapshot = this._viewModel.Stacks.Select(stack => stack.Model).ToArray();
            var matches = await Task.Run(() => StackCatalogSearch.Find(snapshot, query, token), token);
            if (token.IsCancellationRequested || !ReferenceEquals(this._searchCancellation, cancellation) || !this._isShowingSearch)
            {
                return;
            }

            foreach (var result in this.CreateSearchResults(matches))
            {
                this.SearchResults.Add(result);
            }

            this.SearchResultsList.ItemsSource = new CollectionViewSource
            {
                IsSourceGrouped = true,
                Source = StackSearchResultGroup.Create(this.SearchResults)
            }.View;

            var stackCount = this.SearchResults.Count(result => result.Item is null);
            var itemCount = this.SearchResults.Count - stackCount;
            this.SearchSummaryText.Text = $"“{query}” · {stackCount} {(stackCount == 1 ? "stack" : "stacks")} · {itemCount} {(itemCount == 1 ? "item" : "items")} · All locations";
            this.SearchEmptyTitleText.Text = "No results";
            this.SearchEmptyDescriptionText.Text = "Try a stack name, item name, path, URL, or saved text.";
            this.SearchEmptyState.Visibility = this.SearchResults.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            var selected = this.SearchResults.FirstOrDefault(result =>
                result.Stack.Model.Id == this._lastSearchStackId && result.Item?.Model.Id == this._lastSearchItemId);
            this.SearchResultsList.SelectedItem = selected ?? this.SearchResults.FirstOrDefault();
            if (selected is not null)
            {
                this.SearchResultsList.ScrollIntoView(selected);
            }

            if (focusResults && this.SearchResults.Count > 0)
            {
                this.SearchResultsList.Focus(FocusState.Programmatic);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (ReferenceEquals(this._searchCancellation, cancellation) && this._isShowingSearch)
            {
                this.SearchEmptyTitleText.Text = "Search could not be completed";
                this.SearchEmptyDescriptionText.Text = exception.Message;
                this.SearchSummaryText.Text = $"“{query}” · All locations";
                this.SearchEmptyState.Visibility = Visibility.Visible;
            }
        }
        finally
        {
            if (ReferenceEquals(this._searchCancellation, cancellation))
            {
                this.SearchProgressRing.IsActive = false;
                this.SearchProgressRing.Visibility = Visibility.Collapsed;
            }
        }
    }

    private List<StackSearchResultViewModel> CreateSearchResults(IReadOnlyList<StackSearchMatch> matches)
    {
        var stacks = this._viewModel.Stacks.ToDictionary(stack => stack.Model.Id);
        var items = this._viewModel.Stacks.SelectMany(stack => stack.Items.Select(item =>
            (Key: (stack.Model.Id, item.Model.Id), Item: item))).ToDictionary(pair => pair.Key, pair => pair.Item);
        var results = new List<StackSearchResultViewModel>();
        foreach (var match in matches)
        {
            if (!stacks.TryGetValue(match.StackId, out var stack))
            {
                continue;
            }

            DropItemViewModel? item = null;
            if (match.ItemId is { } itemId && !items.TryGetValue((match.StackId, itemId), out item))
            {
                continue;
            }

            results.Add(new StackSearchResultViewModel(stack, item, match.Preview));
        }

        return results;
    }

    private void OnSearchResultClick(object sender, ItemClickEventArgs args)
    {
        if (args.ClickedItem is StackSearchResultViewModel result)
        {
            this.OpenSearchResult(result);
        }
    }

    private void OpenSearchResult(StackSearchResultViewModel result)
    {
        var stack = this._viewModel.Stacks.FirstOrDefault(candidate => candidate.Model.Id == result.Stack.Model.Id);
        var itemId = result.Item?.Model.Id;
        if (stack is null || (itemId is { } id && stack.Items.All(item => item.Model.Id != id)))
        {
            App.Current.ShowToast("That search result is no longer available.", InfoBarSeverity.Warning);
            this.ShowSearchResults();
            return;
        }

        this._lastSearchStackId = stack.Model.Id;
        this._lastSearchItemId = itemId;
        this.OpenStack(stack, fromSearch: true);
        if (itemId is { } selectedItemId)
        {
            this.ItemsOrganizer.SelectItem(selectedItemId);
        }
    }

    private void OnBackFromSearchClick(object sender, RoutedEventArgs args)
    {
        var stack = this._viewModel.Stacks.FirstOrDefault(candidate => candidate.Model.Id == this._searchOriginStackId);
        var itemId = this._searchOriginItemId;
        if (stack is null)
        {
            this.ShowOverview();
            return;
        }

        this.OpenStack(stack);
        if (itemId is { } id)
        {
            this.ItemsOrganizer.SelectItem(id);
        }
    }

    private void LeaveSearchResults(bool retainSearchNavigation)
    {
        this.CancelSearchRequests();
        this._isShowingSearch = false;
        this._openedFromSearch = retainSearchNavigation;
        this.BrowserContent.Visibility = Visibility.Visible;
        this.SearchContent.Visibility = Visibility.Collapsed;
        var backLabel = retainSearchNavigation ? "Back to search results" : "Back to stacks";
        AutomationProperties.SetName(this.StackBackButton, backLabel);
        ToolTipService.SetToolTip(this.StackBackButton, backLabel);
        if (!retainSearchNavigation)
        {
            this._searchQuery = string.Empty;
            this._searchOriginStackId = null;
            this._searchOriginItemId = null;
            this._lastSearchStackId = null;
            this._lastSearchItemId = null;
            this.SearchResults.Clear();
            this.SearchResultsList.ItemsSource = null;
        }
    }

    private void ClearSearchSuggestions()
    {
        CancelRequest(ref this._suggestionCancellation);
        this.GlobalSearchBox.IsSuggestionListOpen = false;
        this.GlobalSearchBox.ItemsSource = null;
        this._searchPopupFooter.Visibility = Visibility.Collapsed;
        this._searchPopupEmptyState.Visibility = Visibility.Collapsed;
    }

    private void CancelSearchRequests()
    {
        CancelRequest(ref this._searchCancellation);
        this.ClearSearchSuggestions();
        this.SearchProgressRing.IsActive = false;
        this.SearchProgressRing.Visibility = Visibility.Collapsed;
    }

    private static void CancelRequest(ref CancellationTokenSource? cancellation)
    {
        cancellation?.Cancel();
        cancellation?.Dispose();
        cancellation = null;
    }
}
