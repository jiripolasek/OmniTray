// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

namespace OmniTray.CommandPalette.Pages;

internal sealed partial class OmniTrayStacksPage : DynamicListPage, IDisposable
{
    private readonly OmniTrayCatalogSource _catalog;
    private readonly Lock _syncRoot = new();
    private bool _isDisposed;
    private IListItem[] _items = [];

    internal OmniTrayStacksPage(OmniTrayCatalogSource catalog)
    {
        this._catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        this.Id = "OmniTray.Stacks";
        this.Name = "OmniTray stacks";
        this.Title = "OmniTray stacks";
        this.Icon = Icons.Main;
        this.PlaceholderText = "Search stack names, item names, paths, or saved text";
        this.ShowDetails = true;
        this.IsLoading = !this._catalog.IsInitialized;
        this.EmptyContent = CreateStatusItem("Loading OmniTray stacks…", "Reading OmniTray's saved catalog");

        this._catalog.Changed += this.OnCatalogChanged;
        this.Refresh(string.Empty);
    }

    public void Dispose()
    {
        if (this._isDisposed)
        {
            return;
        }

        this._isDisposed = true;
        this._catalog.Changed -= this.OnCatalogChanged;
    }

    public override IListItem[] GetItems()
    {
        lock (this._syncRoot)
        {
            return this._items;
        }
    }

    public override void UpdateSearchText(string oldSearch, string newSearch)
    {
        if (!StringComparer.Ordinal.Equals(oldSearch, newSearch))
        {
            this.Refresh(newSearch);
        }
    }

    private void OnCatalogChanged(object? sender, EventArgs args) => this.Refresh(this.SearchText);

    private void Refresh(string? query)
    {
        if (this._isDisposed)
        {
            return;
        }

        if (!this._catalog.IsInitialized)
        {
            this.IsLoading = true;
            return;
        }

        var stacks = this._catalog.GetSnapshot();
        var items = stacks
            .Where(stack => StackFilter.Matches(stack, query))
            .Select(static stack => (IListItem)new StackListItem(stack))
            .ToArray();
        lock (this._syncRoot)
        {
            this._items = items;
        }

        this.IsLoading = false;
        this.EmptyContent = items.Length == 0
            ? CreateEmptyContent(stacks.Count == 0, query, this._catalog.StatusMessage)
            : null;
        this.RaiseItemsChanged(items.Length);
    }

    private static CommandItem CreateEmptyContent(
        bool catalogIsEmpty,
        string? query,
        string? statusMessage)
    {
        if (!string.IsNullOrWhiteSpace(statusMessage))
        {
            return CreateStatusItem("No OmniTray stacks available", statusMessage);
        }

        return string.IsNullOrWhiteSpace(query)
            ? CreateStatusItem(
                catalogIsEmpty ? "No stacks yet" : "No stacks available",
                "Create a stack in OmniTray, then return here.")
            : CreateStatusItem("No matching stacks", "Try another name, path, item type, or text fragment.");
    }

    private static CommandItem CreateStatusItem(string title, string subtitle) => new()
    {
        Title = title, Subtitle = subtitle, Icon = Icons.Main
    };
}
