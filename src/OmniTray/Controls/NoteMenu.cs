// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

using Microsoft.UI.Dispatching;

namespace OmniTray.Controls;

public sealed class NoteMenu : DependencyObject
{
    public static readonly DependencyProperty StackProperty = DependencyProperty.RegisterAttached(
        "Stack", typeof(DropStackViewModel), typeof(NoteMenu), new PropertyMetadata(null, OnStackChanged));

    public static readonly DependencyProperty ShowAsSubmenuProperty = DependencyProperty.RegisterAttached(
        "ShowAsSubmenu", typeof(bool), typeof(NoteMenu), new PropertyMetadata(true));

    public static bool GetShowAsSubmenu(DependencyObject element) =>
        (bool)element.GetValue(ShowAsSubmenuProperty);

    public static void SetShowAsSubmenu(DependencyObject element, bool value) =>
        element.SetValue(ShowAsSubmenuProperty, value);

    public static DropStackViewModel? GetStack(DependencyObject element) =>
        (DropStackViewModel?)element.GetValue(StackProperty);

    public static void SetStack(DependencyObject element, DropStackViewModel? value) =>
        element.SetValue(StackProperty, value);

    private static void OnStackChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is MenuFlyout menu)
        {
            menu.Opening -= OnStackMenuOpening;
            if (args.NewValue is not null)
            {
                menu.Opening += OnStackMenuOpening;
            }
        }
    }

    private static void OnStackMenuOpening(object? sender, object args)
    {
        if (sender is not MenuFlyout menu || GetStack(menu) is not { } stack)
        {
            return;
        }

        var stackNotes = NoteOperations.GetStackNotes(stack.Model);
        IList<MenuFlyoutItemBase> items;
        if (GetShowAsSubmenu(menu))
        {
            var notes = menu.Items.OfType<MenuFlyoutSubItem>().FirstOrDefault(item => item.Tag is NoteMenu);
            if (notes is null)
            {
                notes = new MenuFlyoutSubItem
                {
                    Text = "Notes", Icon = new FontIcon { Glyph = "\uE70B" }, Tag = new NoteMenu()
                };
                menu.Items.Insert(0, notes);
            }

            notes.Text = stackNotes.Count > 0 ? $"Notes ({stackNotes.Count})" : "Notes";
            items = notes.Items;
        }
        else
        {
            // A dedicated Notes button already supplies the grouping.
            items = menu.Items;
        }

        items.Clear();
        AddAction(menu, items, "New note", () =>
                App.Current.CreateNote(new NoteTarget(stack.Model.Id, NotePlacement.StackItem)),
            new FontIcon { Glyph = "\uE70B" });
        AddAction(menu, items, "Clipboard → note", () =>
            _ = App.Current.CreateClipboardNoteAsync(stack.Model.Id), new SymbolIcon(Symbol.Paste));
        AddExisting(menu, items, stackNotes);
        items.Add(new MenuFlyoutSeparator());
        AddAction(menu, items, "Browse all notes", () => App.Current.ShowNotes(), new FontIcon { Glyph = "\uE70B" });
        AddAction(menu, items, "Recently deleted notes", () => App.Current.ShowNotes(true),
            new SymbolIcon(Symbol.Undo));
    }

    internal static void PopulateItemMenu(MenuFlyout menu, DropStackViewModel stack, DropItem item)
    {
        menu.Items.Clear();
        AddAction(menu, menu.Items, "Attach a note to item", () =>
                App.Current.CreateNote(new NoteTarget(stack.Model.Id, NotePlacement.Item, item.Id)),
            new FontIcon { Glyph = "\uE70B" });
        AddExisting(menu, menu.Items, item.AttachedNotes);
    }

    private static void AddExisting(MenuFlyout menu, IList<MenuFlyoutItemBase> items, IReadOnlyList<StickyNote> notes)
    {
        if (notes.Count > 0)
        {
            items.Add(new MenuFlyoutSeparator());
        }

        foreach (var note in notes)
        {
            AddAction(menu, items, note.DisplayName, () => App.Current.ShowNote(note.Id),
                new FontIcon { Glyph = "\uE70B" });
        }
    }

    private static void AddAction(
        MenuFlyout menu,
        IList<MenuFlyoutItemBase> items,
        string text,
        Action action,
        IconElement icon)
    {
        var item = new MenuFlyoutItem { Text = text, Icon = icon };
        item.Click += (_, _) =>
        {
            menu.Hide();
            DispatcherQueue.GetForCurrentThread().TryEnqueue(DispatcherQueuePriority.Low, () => action());
        };
        items.Add(item);
    }
}
