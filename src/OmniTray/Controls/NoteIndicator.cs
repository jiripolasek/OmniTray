// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.ComponentModel;
using Microsoft.UI.Xaml.Automation;

namespace OmniTray.Controls;

public sealed partial class NoteIndicator : Button
{
    public static readonly DependencyProperty StackProperty = DependencyProperty.Register(nameof(Stack),
        typeof(DropStackViewModel), typeof(NoteIndicator), new PropertyMetadata(null, OnChanged));
    public static readonly DependencyProperty ItemProperty = DependencyProperty.Register(nameof(Item),
        typeof(DropItemViewModel), typeof(NoteIndicator), new PropertyMetadata(null, OnChanged));

    private DropStackViewModel? _subscribedStack;
    private readonly TextBlock _count = new() { FontSize = 12 };

    public NoteIndicator()
    {
        this.Padding = new Thickness(5, 2, 5, 2);
        this.MinWidth = 28;
        this.MinHeight = 28;
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        content.Children.Add(new FontIcon { Glyph = "\uE70B", FontSize = 13 });
        content.Children.Add(this._count);
        this.Content = content;
        this.Loaded += (_, _) => this.Subscribe();
        this.Unloaded += (_, _) => this.Unsubscribe();
        this.Click += this.OnClick;
        this.Visibility = Visibility.Collapsed;
    }

    public DropStackViewModel? Stack
    {
        get => (DropStackViewModel?)this.GetValue(StackProperty);
        set => this.SetValue(StackProperty, value);
    }

    public DropItemViewModel? Item
    {
        get => (DropItemViewModel?)this.GetValue(ItemProperty);
        set => this.SetValue(ItemProperty, value);
    }

    private IReadOnlyList<StickyNote> Notes => this.Item is { } item ? item.Model.AttachedNotes
        : this.Stack is { } stack ? NoteOperations.GetStackNotes(stack.Model) : [];

    private static void OnChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var indicator = (NoteIndicator)sender;
        if (indicator.IsLoaded)
        {
            indicator.Subscribe();
        }
        indicator.Refresh();
    }

    private void Subscribe()
    {
        this.Unsubscribe();
        this._subscribedStack = this.Stack;
        if (this._subscribedStack is not null)
        {
            this._subscribedStack.PropertyChanged += this.OnStackChanged;
        }
        this.Refresh();
    }

    private void Unsubscribe()
    {
        if (this._subscribedStack is not null)
        {
            this._subscribedStack.PropertyChanged -= this.OnStackChanged;
            this._subscribedStack = null;
        }
    }

    private void OnStackChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(DropStackViewModel.Model)) { this.Refresh(); }
    }

    private void Refresh()
    {
        var notes = this.Notes;
        this.Visibility = notes.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        this._count.Text = notes.Count.ToString();
        AutomationProperties.SetName(this, $"{notes.Count} {(this.Item is null ? "notes in stack" : "attached notes")}. Open notes.");
        ToolTipService.SetToolTip(this, string.Join("\n", notes.Take(3).Select(note =>
            note.Text.Length > 140 ? note.Text[..140] + "…" : string.IsNullOrWhiteSpace(note.Text) ? "New note" : note.Text)));
    }

    private void OnClick(object sender, RoutedEventArgs args)
    {
        var notes = this.Notes;
        if (notes.Count == 1)
        {
            App.Current.ShowNote(notes[0].Id);
            return;
        }
        var menu = new MenuFlyout();
        foreach (var note in notes)
        {
            var command = new MenuFlyoutItem { Text = note.DisplayName, Icon = new FontIcon { Glyph = "\uE70B" } };
            command.Click += (_, _) => App.Current.ShowNote(note.Id);
            menu.Items.Add(command);
        }
        if (notes.Count > 0)
        {
            menu.ShowAt(this);
        }
    }
}
