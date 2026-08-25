// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

namespace OmniTray.CommandPalette.Pages;

internal sealed partial class StackItemsPage : DynamicListPage
{
    private readonly DropStack _stack;
    private IListItem[] _items = [];

    internal StackItemsPage(DropStack stack)
    {
        this._stack = stack ?? throw new ArgumentNullException(nameof(stack));
        this.Id = $"OmniTray.Stack.{stack.Id:D}";
        this.Name = stack.Name;
        this.Title = stack.Name;
        this.Icon = Icons.Stack;
        this.PlaceholderText = "Search this stack";
        this.ShowDetails = true;
        this.Refresh(string.Empty);
    }

    public override IListItem[] GetItems() => this._items;

    public override void UpdateSearchText(string oldSearch, string newSearch)
    {
        if (!StringComparer.Ordinal.Equals(oldSearch, newSearch))
        {
            this.Refresh(newSearch);
        }
    }

    private void Refresh(string? query)
    {
        var terms = query?.Split(
            default(char[]),
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
        this._items = this._stack.Items
            .Where(item => terms.All(term => Matches(item, term)))
            .Select(item => (IListItem)new StackItemListItem(this._stack.Id, item))
            .ToArray();
        this.EmptyContent = this._items.Length == 0
            ? new CommandItem
            {
                Title = this._stack.Items.Count == 0 ? "This stack is empty" : "No matching items",
                Subtitle = this._stack.Items.Count == 0
                    ? "Drop content into the tray or create another stack."
                    : "Try another item name, type, path, or text fragment.",
                Icon = Icons.Stack
            }
            : null;
        this.RaiseItemsChanged(this._items.Length);
    }

    private static bool Matches(DropItem item, string term) =>
        item.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
        item.Kind.ToString().Contains(term, StringComparison.OrdinalIgnoreCase) ||
        (item.SourcePath?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
        (item.Text?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false);
}
