// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

namespace OmniTray.Core;

public static class StackOperations
{
    public static DropStack InsertItems(
        DropStack target,
        IEnumerable<DropItem> items,
        int targetIndex)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(items);

        if (targetIndex < 0 || targetIndex > target.Items.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(targetIndex));
        }

        var additions = items.ToArray();
        if (additions.Length == 0)
        {
            throw new ArgumentException("At least one item is required.", nameof(items));
        }

        if (additions.Select(static item => item.Id).Distinct().Count() != additions.Length ||
            additions.Any(addition => target.Items.Any(item => item.Id == addition.Id)))
        {
            throw new ArgumentException(
                "Inserted items must have unique IDs that are not already in the target stack.",
                nameof(items));
        }

        var result = target.Items.ToList();
        result.InsertRange(targetIndex, additions);
        return target.WithItems(result);
    }

    public static DropStack MoveItemsWithin(
        DropStack stack,
        IEnumerable<Guid> itemIds,
        int targetIndex)
    {
        ArgumentNullException.ThrowIfNull(stack);
        var selectedIds = ValidateSelectedItems(stack, itemIds);
        if (targetIndex < 0 || targetIndex > stack.Items.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(targetIndex));
        }

        var selected = stack.Items
            .Where(item => selectedIds.Contains(item.Id))
            .ToArray();
        var selectedBeforeTarget = stack.Items
            .Take(targetIndex)
            .Count(item => selectedIds.Contains(item.Id));
        var remaining = stack.Items
            .Where(item => !selectedIds.Contains(item.Id))
            .ToList();
        remaining.InsertRange(targetIndex - selectedBeforeTarget, selected);
        return stack.WithItems(remaining);
    }

    public static (DropStack Source, DropStack Target) MoveItems(
        DropStack source,
        DropStack target,
        IEnumerable<Guid> itemIds,
        int targetIndex)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        if (source.Id == target.Id)
        {
            throw new ArgumentException(
                "Use MoveItemsWithin when the source and target are the same stack.",
                nameof(target));
        }

        if (targetIndex < 0 || targetIndex > target.Items.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(targetIndex));
        }

        var selectedIds = ValidateSelectedItems(source, itemIds);
        var movedItems = source.Items
            .Where(item => selectedIds.Contains(item.Id))
            .ToArray();
        if (movedItems.Any(moved => target.Items.Any(item => item.Id == moved.Id)))
        {
            throw new ArgumentException(
                "The target stack already contains one of the selected item IDs.",
                nameof(target));
        }

        var remainingSource = source.WithItems(
            source.Items.Where(item => !selectedIds.Contains(item.Id)));
        var updatedTarget = InsertItems(target, movedItems, targetIndex);
        return (remainingSource, updatedTarget);
    }

    public static DropStack Combine(IEnumerable<DropStack> stacks)
    {
        ArgumentNullException.ThrowIfNull(stacks);

        var snapshot = stacks.ToArray();
        if (snapshot.Length == 0)
        {
            throw new ArgumentException("At least one stack is required.", nameof(stacks));
        }

        var items = snapshot.SelectMany(static stack => stack.Items).ToArray();
        return items.Length == 0 ? DropStack.CreateEmpty() : DropStack.Create(items);
    }

    public static DropStack CombineInto(
        DropStack target,
        IEnumerable<DropStack> sourceStacks)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(sourceStacks);

        var sources = sourceStacks.ToArray();
        if (sources.Length == 0)
        {
            throw new ArgumentException("At least one source stack is required.", nameof(sourceStacks));
        }

        if (sources.Any(source => source.Id == target.Id) ||
            sources.Select(static source => source.Id).Distinct().Count() != sources.Length)
        {
            throw new ArgumentException(
                "Source stacks must be unique and cannot include the target stack.",
                nameof(sourceStacks));
        }

        var additions = sources.SelectMany(static stack => stack.Items).ToArray();
        return additions.Length == 0 ? target : target.Append(additions);
    }

    public static (DropStack Remaining, DropStack Extracted) Split(
        DropStack source,
        IEnumerable<Guid> selectedItemIds)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(selectedItemIds);

        var selected = selectedItemIds.ToHashSet();
        var extractedItems = source.Items.Where(item => selected.Contains(item.Id)).ToArray();
        var remainingItems = source.Items.Where(item => !selected.Contains(item.Id)).ToArray();

        if (extractedItems.Length == 0 || remainingItems.Length == 0)
        {
            throw new ArgumentException(
                "A split must select at least one, but not all, items from the source stack.",
                nameof(selectedItemIds));
        }

        return (
            source.WithItems(remainingItems),
            DropStack.Create(
                extractedItems,
                tint: source.Tint,
                inspectorViewMode: source.InspectorViewMode));
    }

    private static HashSet<Guid> ValidateSelectedItems(
        DropStack stack,
        IEnumerable<Guid> itemIds)
    {
        ArgumentNullException.ThrowIfNull(itemIds);

        var requested = itemIds.ToArray();
        if (requested.Length == 0 || requested.Distinct().Count() != requested.Length)
        {
            throw new ArgumentException(
                "At least one unique item ID is required.",
                nameof(itemIds));
        }

        var availableIds = stack.Items.Select(static item => item.Id).ToHashSet();
        if (requested.Any(id => !availableIds.Contains(id)))
        {
            throw new ArgumentException(
                "Every selected item must belong to the source stack.",
                nameof(itemIds));
        }

        return requested.ToHashSet();
    }
}
