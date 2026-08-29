// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

using System.Collections.ObjectModel;
using WinRT;

namespace OmniTray.ViewModels.Organizer;

[GeneratedBindableCustomProperty]
public sealed partial class StackSearchResultViewModel(
    DropStackViewModel stack,
    DropItemViewModel? item,
    string preview,
    StickyNote? note = null)
{
    public DropStackViewModel Stack { get; } = stack;

    public DropItemViewModel? Item { get; } = item;

    public StickyNote? Note { get; } = note;

    public string Title => this.Note?.DisplayName ?? this.Item?.DisplayName ?? this.Stack.Name;

    public string Location => this.Note is not null
        ? $"Note · {this.Stack.Name}" + (this.Item is not null && this.Item.Model.Kind != DropItemKind.Note
            ? $" · {this.Item.DisplayName}"
            : "")
        : this.Item is null
            ? $"Stack · {this.Stack.ItemCountText} · {this.Stack.EdgePlacementText}"
            : $"{this.Item.KindLabel} · {this.Stack.Name} · {this.Stack.EdgePlacementText}";

    public string Preview { get; } = preview;

    public Visibility StackVisibility =>
        this.Item is null && this.Note is null ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ItemVisibility =>
        this.Item is null || this.Note is not null ? Visibility.Collapsed : Visibility.Visible;

    public Visibility PreviewVisibility =>
        string.IsNullOrWhiteSpace(this.Preview) ? Visibility.Collapsed : Visibility.Visible;

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
        var stacks = snapshot.Where(result => result.Item is null && result.Note is null).Take(stackLimit).ToArray();
        var items = snapshot.Where(result => result.Item is not null && result.Note is null).Take(itemLimit).ToArray();
        var notes = snapshot.Where(result => result.Note is not null).Take(itemLimit).ToArray();
        if (stacks.Length > 0)
        {
            groups.Add(new StackSearchResultGroup("Stacks", stacks));
        }

        if (items.Length > 0)
        {
            groups.Add(new StackSearchResultGroup("Items", items));
        }

        if (notes.Length > 0)
        {
            groups.Add(new StackSearchResultGroup("Notes", notes));
        }

        return groups;
    }
}
