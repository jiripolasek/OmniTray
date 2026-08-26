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

    private DropStack(
        Guid id,
        string name,
        string tint,
        IReadOnlyList<DropItem> items,
        StackInspectorViewMode inspectorViewMode)
    {
        if (items.Select(static item => item.Id).Distinct().Count() != items.Count)
        {
            throw new ArgumentException("Item IDs must be unique within a stack.", nameof(items));
        }

        if (!Enum.IsDefined(inspectorViewMode))
        {
            throw new ArgumentOutOfRangeException(nameof(inspectorViewMode));
        }

        this.Id = id;
        this.Name = name;
        this.Tint = tint;
        this.Items = items;
        this.InspectorViewMode = inspectorViewMode;
    }

    public Guid Id { get; }

    public string Name { get; }

    public string Tint { get; }

    public IReadOnlyList<DropItem> Items { get; }

    public StackInspectorViewMode InspectorViewMode { get; }

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
            this.InspectorViewMode);
    }

    public DropStack Rename(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new DropStack(this.Id, name.Trim(), this.Tint, this.Items, this.InspectorViewMode);
    }

    public DropStack ChangeTint(string tint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tint);
        return new DropStack(this.Id, this.Name, tint.Trim(), this.Items, this.InspectorViewMode);
    }

    public DropStack ChangeInspectorViewMode(StackInspectorViewMode inspectorViewMode)
    {
        if (!Enum.IsDefined(inspectorViewMode))
        {
            throw new ArgumentOutOfRangeException(nameof(inspectorViewMode));
        }

        return inspectorViewMode == this.InspectorViewMode
            ? this
            : new DropStack(this.Id, this.Name, this.Tint, this.Items, inspectorViewMode);
    }

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
            this.Items.Where(item => !removals.Contains(item.Id)).ToArray(),
            this.InspectorViewMode);
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
            this.InspectorViewMode);
    }

    internal DropStack WithItems(IEnumerable<DropItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return new DropStack(this.Id, this.Name, this.Tint, items.ToArray(), this.InspectorViewMode);
    }

    public static DropStack Create(
        IEnumerable<DropItem> items,
        string? name = null,
        string tint = DefaultTint,
        StackInspectorViewMode inspectorViewMode = StackInspectorViewMode.List)
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

        return new DropStack(Guid.NewGuid(), resolvedName, tint.Trim(), snapshot, inspectorViewMode);
    }

    public static DropStack CreateEmpty(
        string name = "New stack",
        string tint = DefaultTint,
        StackInspectorViewMode inspectorViewMode = StackInspectorViewMode.List)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(tint);
        return new DropStack(Guid.NewGuid(), name.Trim(), tint.Trim(), [], inspectorViewMode);
    }

    public static DropStack Restore(
        Guid id,
        string name,
        string tint,
        IEnumerable<DropItem> items,
        StackInspectorViewMode inspectorViewMode = StackInspectorViewMode.List)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("A stack ID is required.", nameof(id));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(tint);
        ArgumentNullException.ThrowIfNull(items);

        return new DropStack(id, name.Trim(), tint.Trim(), items.ToArray(), inspectorViewMode);
    }
}
