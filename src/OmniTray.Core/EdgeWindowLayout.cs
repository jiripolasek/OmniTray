// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

namespace OmniTray.Core;

public sealed record EdgeWindowLayout
{
    public Guid Id { get; }

    public string Name { get; }

    public IReadOnlyList<Guid> StackIds { get; }

    private EdgeWindowLayout(Guid id, string name, IReadOnlyList<Guid> stackIds)
    {
        this.Id = id;
        this.Name = name;
        this.StackIds = stackIds;
    }

    public static EdgeWindowLayout Create(string name, IEnumerable<Guid>? stackIds = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var snapshot = stackIds?.ToArray() ?? [];
        if (snapshot.Any(static id => id == Guid.Empty) || snapshot.Distinct().Count() != snapshot.Length)
        {
            throw new ArgumentException("Stack IDs must be non-empty and unique within an edge window.",
                nameof(stackIds));
        }

        return new EdgeWindowLayout(Guid.NewGuid(), name.Trim(), snapshot);
    }

    internal EdgeWindowLayout WithStackIds(IReadOnlyList<Guid> stackIds) => new(this.Id, this.Name, stackIds);

    public EdgeWindowLayout ReorderStacks(IEnumerable<Guid> orderedStackIds)
    {
        ArgumentNullException.ThrowIfNull(orderedStackIds);
        var order = orderedStackIds.ToArray();
        if (order.Length != this.StackIds.Count ||
            order.Distinct().Count() != order.Length ||
            order.Any(id => !this.StackIds.Contains(id)))
        {
            throw new ArgumentException(
                "The stack order must contain every stack ID exactly once.",
                nameof(orderedStackIds));
        }

        return this.WithStackIds(order);
    }
}

public sealed record EdgeLayout
{
    public IReadOnlyList<EdgeWindowLayout> Windows { get; }

    private EdgeLayout(IReadOnlyList<EdgeWindowLayout> windows)
    {
        this.Windows = windows;
    }

    public static EdgeLayout Create(IEnumerable<EdgeWindowLayout> windows)
    {
        ArgumentNullException.ThrowIfNull(windows);
        var snapshot = windows.ToArray();
        if (snapshot.Select(static window => window.Id).Distinct().Count() != snapshot.Length)
        {
            throw new ArgumentException("Edge window IDs must be unique.", nameof(windows));
        }

        var duplicateStack = snapshot
            .SelectMany(static window => window.StackIds)
            .GroupBy(static stackId => stackId)
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicateStack is not null)
        {
            throw new ArgumentException("A stack can belong to at most one edge window.", nameof(windows));
        }

        return new EdgeLayout(snapshot);
    }

    public EdgeLayout AssignStack(Guid stackId, Guid edgeWindowId)
    {
        if (stackId == Guid.Empty)
        {
            throw new ArgumentException("A stack ID cannot be empty.", nameof(stackId));
        }

        if (!this.Windows.Any(window => window.Id == edgeWindowId))
        {
            throw new ArgumentException("The target edge window does not exist.", nameof(edgeWindowId));
        }

        return new EdgeLayout(this.Windows.Select(window =>
        {
            var stackIds = window.StackIds.Where(id => id != stackId).ToList();
            if (window.Id == edgeWindowId)
            {
                stackIds.Add(stackId);
            }

            return window.WithStackIds(stackIds);
        }).ToArray());
    }

    public EdgeLayout RemoveStack(Guid stackId) => new(this.Windows
        .Select(window => window.WithStackIds(window.StackIds.Where(id => id != stackId).ToArray()))
        .ToArray());
}
