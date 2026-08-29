// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

using System.ComponentModel;
using Microsoft.UI.Xaml.Data;
using OmniTray.ViewModels.Organizer;

namespace OmniTray.Views.Organizer;

public sealed partial class StackSearchPage : Page, IDisposable
{
    internal event EventHandler? BackRequested;
    internal event EventHandler<StackSearchResultViewModel>? ResultOpened;
    private bool _disposed;

    public StackSearchViewModel ViewModel { get; }
    public CollectionViewSource ResultsSource { get; } = new() { IsSourceGrouped = true };

    internal StackSearchPage(StackSearchViewModel viewModel)
    {
        this.ViewModel = viewModel;
        this.InitializeComponent();
        this.ViewModel.PropertyChanged += this.OnViewModelPropertyChanged;
    }

    internal async Task RefreshAsync(StackOrganizerNavigationState navigation, bool focusResults = false)
    {
        if (!await this.ViewModel.RefreshAsync(navigation.SearchQuery, navigation.LastSearchStackId,
                navigation.LastSearchItemId))
        {
            return;
        }

        if (this._disposed || navigation.Page != StackOrganizerPage.Search) { return; }

        this.SearchResultsList.SelectedItem = this.ViewModel.SelectedResult;
        if (this.ViewModel.SelectedResult is { } selected)
        {
            this.SearchResultsList.ScrollIntoView(selected);
            if (focusResults) { this.SearchResultsList.Focus(FocusState.Programmatic); }
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(StackSearchViewModel.Groups))
        {
            this.ResultsSource.Source = this.ViewModel.Groups;
        }
    }

    private void OnBackFromSearchClick(object sender, RoutedEventArgs args) =>
        this.BackRequested?.Invoke(this, EventArgs.Empty);

    private void OnSearchResultClick(object sender, ItemClickEventArgs args)
    {
        if (args.ClickedItem is StackSearchResultViewModel result) { this.ResultOpened?.Invoke(this, result); }
    }

    public void Dispose()
    {
        this._disposed = true;
        this.ViewModel.PropertyChanged -= this.OnViewModelPropertyChanged;
        this.ResultsSource.Source = null;
    }
}
