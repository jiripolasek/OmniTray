// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

namespace OmniTray.ViewModels.Organizer;

public sealed partial class StackSearchViewModel(MainViewModel catalog) : ObservableObject, IDisposable
{
    private readonly MainViewModel _catalog = catalog;
    private CancellationTokenSource? _searchCancellation;
    private CancellationTokenSource? _suggestionCancellation;
    private bool _disposed;

    [ObservableProperty]
    public partial IReadOnlyList<StackSearchResultGroup> Groups { get; private set; } = [];

    [ObservableProperty]
    public partial string Summary { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string EmptyTitle { get; private set; } = "No results";

    [ObservableProperty]
    public partial string EmptyDescription { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsBusy { get; private set; }

    [ObservableProperty]
    public partial bool IsEmpty { get; private set; }
    public StackSearchResultViewModel? SelectedResult { get; private set; }

    internal async Task<IReadOnlyList<StackSearchResultGroup>?> FindSuggestionsAsync(string query)
    {
        this.CancelSuggestions();
        if (this._disposed || string.IsNullOrWhiteSpace(query)) { return null; }
        var cancellation = this._suggestionCancellation = new CancellationTokenSource();
        var token = cancellation.Token;
        try
        {
            await Task.Delay(150, token);
            var snapshot = this._catalog.Stacks.Select(stack => stack.Model).ToArray();
            var matches = await Task.Run(() => StackCatalogSearch.Find(snapshot, query, token), token);
            return token.IsCancellationRequested || !ReferenceEquals(this._suggestionCancellation, cancellation) || this._disposed
                ? null : StackSearchResultGroup.Create(this.CreateSearchResults(matches), stackLimit: 3, itemLimit: 4);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return null; }
        catch (Exception)
        {
            // Suggestions are optional; the results page reports errors for submitted searches.
            return null;
        }
        finally
        {
            if (ReferenceEquals(this._suggestionCancellation, cancellation))
            {
                this._suggestionCancellation = null;
                cancellation.Dispose();
            }
        }
    }

    internal async Task<bool> RefreshAsync(string query, Guid? selectedStackId, Guid? selectedItemId)
    {
        this.CancelResults();
        if (this._disposed) { return false; }
        var cancellation = this._searchCancellation = new CancellationTokenSource();
        var token = cancellation.Token;
        this.Groups = [];
        this.SelectedResult = null;
        this.IsEmpty = false;
        this.IsBusy = true;
        this.Summary = $"Searching all stacks and items for “{query}”…";
        try
        {
            var snapshot = this._catalog.Stacks.Select(stack => stack.Model).ToArray();
            var matches = await Task.Run(() => StackCatalogSearch.Find(snapshot, query, token), token);
            if (token.IsCancellationRequested || !ReferenceEquals(this._searchCancellation, cancellation) || this._disposed)
            {
                return false;
            }
            var results = this.CreateSearchResults(matches);
            this.Groups = StackSearchResultGroup.Create(results);
            var stackCount = results.Count(result => result.Item is null && result.Note is null);
            var noteCount = results.Count(result => result.Note is not null);
            var itemCount = results.Count - stackCount - noteCount;
            this.Summary = $"“{query}” · {stackCount} {(stackCount == 1 ? "stack" : "stacks")} · {itemCount} {(itemCount == 1 ? "item" : "items")} · {noteCount} {(noteCount == 1 ? "note" : "notes")} · All locations";
            this.EmptyTitle = "No results";
            this.EmptyDescription = "Try a stack name, item name, path, URL, or saved text.";
            this.IsEmpty = results.Count == 0;
            this.SelectedResult = results.FirstOrDefault(result =>
                result.Stack.Model.Id == selectedStackId && result.Item?.Model.Id == selectedItemId) ?? results.FirstOrDefault();
            return true;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return false; }
        catch (Exception exception)
        {
            if (ReferenceEquals(this._searchCancellation, cancellation) && !this._disposed)
            {
                this.EmptyTitle = "Search could not be completed";
                this.EmptyDescription = exception.Message;
                this.Summary = $"“{query}” · All locations";
                this.IsEmpty = true;
            }
            return false;
        }
        finally
        {
            if (ReferenceEquals(this._searchCancellation, cancellation))
            {
                this.IsBusy = false;
                this._searchCancellation = null;
                cancellation.Dispose();
            }
        }
    }

    private List<StackSearchResultViewModel> CreateSearchResults(IReadOnlyList<StackSearchMatch> matches)
    {
        var stacks = this._catalog.Stacks.ToDictionary(stack => stack.Model.Id);
        var items = this._catalog.Stacks.SelectMany(stack => stack.Items.Select(item =>
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

            var note = match.NoteId is { } noteId ? this._catalog.FindNote(noteId)?.Note : null;
            if (match.NoteId is not null && note is null)
            {
                continue;
            }
            results.Add(new StackSearchResultViewModel(stack, item, match.Preview, note));
        }

        return results;
    }

    internal void CancelSuggestions() => CancelRequest(ref this._suggestionCancellation);

    internal void CancelResults(bool clear = false)
    {
        CancelRequest(ref this._searchCancellation);
        this.IsBusy = false;
        if (clear)
        {
            this.Groups = [];
            this.SelectedResult = null;
        }
    }

    private static void CancelRequest(ref CancellationTokenSource? cancellation)
    {
        cancellation?.Cancel();
        cancellation?.Dispose();
        cancellation = null;
    }

    public void Dispose()
    {
        if (this._disposed) { return; }
        this._disposed = true;
        this.CancelSuggestions();
        this.CancelResults(clear: true);
    }
}
