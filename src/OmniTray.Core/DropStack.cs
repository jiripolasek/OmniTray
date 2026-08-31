// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

namespace OmniTray.Core;

public sealed record DropStack
{
    public const string DefaultTint = TrayTintIds.Neutral;
    public const string SystemAccentTint = TrayTintIds.SystemAccent;

    public Guid Id { get; }

    public string Name { get; }

    public string Tint { get; }

    public IReadOnlyList<DropItem> Items { get; }

    public StackInspectorViewMode InspectorViewMode { get; }

    public VirtualStackSource? VirtualSource { get; }

    public StackItemSortMode ItemSortMode { get; }

    private DropStack(
        Guid id,
        string name,
        string tint,
        IReadOnlyList<DropItem> items,
        StackInspectorViewMode inspectorViewMode,
        VirtualStackSource? virtualSource = null,
        StackItemSortMode itemSortMode = StackItemSortMode.Default)
    {
        if (items.Select(static item => item.Id).Distinct().Count() != items.Count)
        {
            throw new ArgumentException("Item IDs must be unique within a stack.", nameof(items));
        }

        if (!Enum.IsDefined(inspectorViewMode))
        {
            throw new ArgumentOutOfRangeException(nameof(inspectorViewMode));
        }

        if (!Enum.IsDefined(itemSortMode))
        {
            throw new ArgumentOutOfRangeException(nameof(itemSortMode));
        }

        this.Id = id;
        this.Name = name;
        this.Tint = tint;
        this.Items = items;
        this.InspectorViewMode = inspectorViewMode;
        this.VirtualSource = virtualSource;
        this.ItemSortMode = itemSortMode;
    }

    public DropStack Append(IEnumerable<DropItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        var additions = items.ToArray();
        if (additions.Length == 0)
        {
            throw new ArgumentException("At least one item is required.", nameof(items));
        }

        return new DropStack(
            this.Id,
            this.Name,
            this.Tint,
            this.Items.Concat(additions).ToArray(),
            this.InspectorViewMode,
            this.VirtualSource,
            this.ItemSortMode);
    }

    public DropStack Rename(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new DropStack(
            this.Id,
            name.Trim(),
            this.Tint,
            this.Items,
            this.InspectorViewMode,
            this.VirtualSource,
            this.ItemSortMode);
    }

    public DropStack ChangeTint(string tint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tint);
        return new DropStack(
            this.Id,
            this.Name,
            tint.Trim(),
            this.Items,
            this.InspectorViewMode,
            this.VirtualSource,
            this.ItemSortMode);
    }

    public DropStack ChangeInspectorViewMode(StackInspectorViewMode inspectorViewMode)
    {
        if (!Enum.IsDefined(inspectorViewMode))
        {
            throw new ArgumentOutOfRangeException(nameof(inspectorViewMode));
        }

        return inspectorViewMode == this.InspectorViewMode
            ? this
            : new DropStack(
                this.Id,
                this.Name,
                this.Tint,
                this.Items,
                inspectorViewMode,
                this.VirtualSource,
                this.ItemSortMode);
    }

    public DropStack ChangeItemSortMode(StackItemSortMode itemSortMode)
    {
        if (!Enum.IsDefined(itemSortMode))
        {
            throw new ArgumentOutOfRangeException(nameof(itemSortMode));
        }

        return itemSortMode == this.ItemSortMode
            ? this
            : new DropStack(
                this.Id,
                this.Name,
                this.Tint,
                this.Items,
                this.InspectorViewMode,
                this.VirtualSource,
                itemSortMode);
    }

    public IReadOnlyList<DropItem> GetItemsInDisplayOrder() => this.ItemSortMode switch
    {
        StackItemSortMode.Name =>
            this.Items.OrderBy(static item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToArray(),
        StackItemSortMode.Newest =>
            this.Items.OrderByDescending(GetItemTimestamp).ToArray(),
        StackItemSortMode.Oldest =>
            this.Items.OrderBy(GetItemTimestamp).ToArray(),
        _ => this.Items
    };

    public DropStack RemoveItems(IEnumerable<Guid> itemIds)
    {
        ArgumentNullException.ThrowIfNull(itemIds);

        var removals = itemIds.ToHashSet();
        if (removals.Count == 0)
        {
            return this;
        }

        return new DropStack(
            this.Id,
            this.Name,
            this.Tint,
            this.Items.Where(item => !removals.Contains(item.Id))
                .Concat(this.Items.Where(item => removals.Contains(item.Id))
                    .SelectMany(static item => item.AttachedNotes).Select(DropItem.CreateNote)).ToArray(),
            this.InspectorViewMode,
            this.VirtualSource,
            this.ItemSortMode);
    }

    public DropStack ReorderItems(IEnumerable<Guid> orderedItemIds)
    {
        ArgumentNullException.ThrowIfNull(orderedItemIds);

        var order = orderedItemIds.ToArray();
        if (order.Length != this.Items.Count ||
            order.Distinct().Count() != order.Length ||
            order.Any(id => this.Items.All(item => item.Id != id)))
        {
            throw new ArgumentException(
                "The item order must contain every item ID exactly once.",
                nameof(orderedItemIds));
        }

        var itemsById = this.Items.ToDictionary(static item => item.Id);
        return new DropStack(
            this.Id,
            this.Name,
            this.Tint,
            order.Select(id => itemsById[id]).ToArray(),
            this.InspectorViewMode,
            this.VirtualSource,
            this.ItemSortMode);
    }

    internal DropStack WithItems(IEnumerable<DropItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return new DropStack(
            this.Id,
            this.Name,
            this.Tint,
            items.ToArray(),
            this.InspectorViewMode,
            this.VirtualSource,
            this.ItemSortMode);
    }

    public DropStack RefreshVirtualItems(IEnumerable<DropItem> items)
    {
        if (this.VirtualSource is null)
        {
            throw new InvalidOperationException("Only a virtual stack can refresh its items.");
        }

        return this.WithItems(items);
    }

    public DropStack ChangeVirtualSource(VirtualStackSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (this.VirtualSource is null)
        {
            throw new InvalidOperationException("Only a virtual stack can change its source.");
        }

        return new DropStack(
            this.Id,
            this.Name,
            this.Tint,
            [],
            this.InspectorViewMode,
            source,
            this.ItemSortMode);
    }

    public static DropStack Create(
        IEnumerable<DropItem> items,
        string? name = null,
        string tint = DefaultTint,
        StackInspectorViewMode inspectorViewMode = StackInspectorViewMode.List,
        StackItemSortMode itemSortMode = StackItemSortMode.Default)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentException.ThrowIfNullOrWhiteSpace(tint);

        var snapshot = items.ToArray();
        if (snapshot.Length == 0)
        {
            throw new ArgumentException("A stack must contain at least one item.", nameof(items));
        }

        var resolvedName = string.IsNullOrWhiteSpace(name)
            ? snapshot.Length == 1 ? snapshot[0].DisplayName : $"{snapshot.Length} items"
            : name.Trim();

        return new DropStack(Guid.NewGuid(), resolvedName, tint.Trim(), snapshot, inspectorViewMode, itemSortMode: itemSortMode);
    }

    public static DropStack CreateEmpty(
        string name = "New stack",
        string tint = DefaultTint,
        StackInspectorViewMode inspectorViewMode = StackInspectorViewMode.List,
        StackItemSortMode itemSortMode = StackItemSortMode.Default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(tint);
        return new DropStack(Guid.NewGuid(), name.Trim(), tint.Trim(), [], inspectorViewMode, itemSortMode: itemSortMode);
    }

    public static DropStack CreateVirtual(
        string name,
        VirtualStackSource source,
        string tint = DefaultTint,
        StackInspectorViewMode inspectorViewMode = StackInspectorViewMode.List,
        StackItemSortMode itemSortMode = StackItemSortMode.Default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(tint);
        return new DropStack(
            Guid.NewGuid(),
            name.Trim(),
            tint.Trim(),
            [],
            inspectorViewMode,
            source,
            itemSortMode);
    }

    public static DropStack Restore(
        Guid id,
        string name,
        string tint,
        IEnumerable<DropItem> items,
        StackInspectorViewMode inspectorViewMode = StackInspectorViewMode.List,
        IReadOnlyList<StickyNote>? attachedNotes = null,
        VirtualStackSource? virtualSource = null,
        StackItemSortMode itemSortMode = StackItemSortMode.Default)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("A stack ID is required.", nameof(id));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(tint);
        ArgumentNullException.ThrowIfNull(items);

        // Older catalogs kept stack notes outside Items. Promote them once at the
        // restore boundary; subsequent saves write only ordinary note items.
        var restoredItems = items.Concat((attachedNotes ?? []).Select(DropItem.CreateNote)).ToArray();
        return new DropStack(id, name.Trim(), tint.Trim(), restoredItems, inspectorViewMode, virtualSource, itemSortMode);
    }

    private static DateTimeOffset GetItemTimestamp(DropItem item) =>
        item.Capture?.CapturedAt ?? item.FileFacts?.ModifiedAt ?? item.CreatedAt;
}
