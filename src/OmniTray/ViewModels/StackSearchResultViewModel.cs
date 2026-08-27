// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Collections.ObjectModel;

namespace OmniTray.ViewModels;

[WinRT.GeneratedBindableCustomProperty]
public sealed partial class StackSearchResultViewModel(DropStackViewModel stack, DropItemViewModel? item, string preview)
{
    public DropStackViewModel Stack { get; } = stack;

    public DropItemViewModel? Item { get; } = item;

    public string Title => this.Item?.DisplayName ?? this.Stack.Name;

    public string Location => this.Item is null
        ? $"Stack · {this.Stack.ItemCountText} · {this.Stack.EdgePlacementText}"
        : $"{this.Item.KindLabel} · {this.Stack.Name} · {this.Stack.EdgePlacementText}";

    public string Preview { get; } = preview;

    public Visibility StackVisibility => this.Item is null ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ItemVisibility => this.Item is null ? Visibility.Collapsed : Visibility.Visible;

    public Visibility PreviewVisibility => string.IsNullOrWhiteSpace(this.Preview) ? Visibility.Collapsed : Visibility.Visible;

    public string AccessibleName => $"{this.Title}, {this.Location}";

    public override string ToString() => this.Title;
}

public sealed partial class StackSearchResultGroup(string title, IEnumerable<StackSearchResultViewModel> items)
    : ObservableCollection<StackSearchResultViewModel>(items)
{
    public string Title { get; } = title;

    public static IReadOnlyList<StackSearchResultGroup> Create(
        IEnumerable<StackSearchResultViewModel> results,
        int stackLimit = int.MaxValue,
        int itemLimit = int.MaxValue)
    {
        var snapshot = results.ToArray();
        var groups = new List<StackSearchResultGroup>();
        var stacks = snapshot.Where(result => result.Item is null).Take(stackLimit).ToArray();
        var items = snapshot.Where(result => result.Item is not null).Take(itemLimit).ToArray();
        if (stacks.Length > 0)
        {
            groups.Add(new StackSearchResultGroup("Stacks", stacks));
        }

        if (items.Length > 0)
        {
            groups.Add(new StackSearchResultGroup("Items", items));
        }

        return groups;
    }
}
