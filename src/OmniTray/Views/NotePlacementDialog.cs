// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

namespace OmniTray.Views;

internal static class NotePlacementDialog
{
    public static async Task ShowAsync(Window owner, MainViewModel catalog, Guid noteId)
    {
        if (catalog.FindNote(noteId) is not { } location)
        {
            return;
        }

        var stacks = new ComboBox { Header = "Stack", HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var stack in catalog.Stacks)
        {
            stacks.Items.Add(new ComboBoxItem { Content = stack.Name, Tag = stack.Model.Id });
        }

        var placement = new ComboBox
        {
            Header = "Placement",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = new[] { "Note in stack", "Attached to item" },
            SelectedIndex = location.Target.Placement == NotePlacement.Item ? 1 : 0
        };
        var items = new ComboBox { Header = "Item", HorizontalAlignment = HorizontalAlignment.Stretch };
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap };

        void UpdateItems()
        {
            items.Items.Clear();
            items.Visibility = placement.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
            if (stacks.SelectedItem is ComboBoxItem { Tag: Guid stackId } &&
                catalog.Stacks.FirstOrDefault(stack => stack.Model.Id == stackId) is { } selected)
            {
                foreach (var item in selected.Model.Items.Where(static item => item.Kind != DropItemKind.Note))
                {
                    items.Items.Add(new ComboBoxItem { Content = item.DisplayName, Tag = item.Id });
                }
            }

            items.SelectedItem = items.Items.OfType<ComboBoxItem>().FirstOrDefault(item =>
                (Guid)item.Tag == location.Target.ItemId) ?? items.Items.FirstOrDefault();
        }

        stacks.SelectionChanged += (_, _) => UpdateItems();
        placement.SelectionChanged += (_, _) => UpdateItems();
        stacks.SelectedItem = stacks.Items.OfType<ComboBoxItem>().FirstOrDefault(item =>
            (Guid)item.Tag == location.Target.StackId);
        UpdateItems();
        var content = new StackPanel { Spacing = 12, Children = { stacks, placement, items, error } };
        await StackDialogWindow.ShowContentAsync(owner, "Move or attach note", content, "Move", 480, () =>
        {
            try
            {
                if (stacks.SelectedItem is not ComboBoxItem { Tag: Guid stackId })
                {
                    error.Text = "Choose a stack.";
                    return false;
                }

                var kind = placement.SelectedIndex == 1 ? NotePlacement.Item : NotePlacement.StackItem;
                var itemId = kind == NotePlacement.Item && items.SelectedItem is ComboBoxItem { Tag: Guid id }
                    ? id
                    : (Guid?)null;
                catalog.MoveNote(noteId, new NoteTarget(stackId, kind, itemId));
                return true;
            }
            catch (ArgumentException exception)
            {
                error.Text = exception.Message;
                return false;
            }
        });
    }
}
