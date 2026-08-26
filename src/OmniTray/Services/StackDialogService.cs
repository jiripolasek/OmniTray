// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

namespace OmniTray.Services;

internal static class StackDialogService
{
    public static async Task<bool> RenameAsync(XamlRoot xamlRoot, DropStackViewModel stack)
    {
        ArgumentNullException.ThrowIfNull(xamlRoot);
        ArgumentNullException.ThrowIfNull(stack);

        var nameBox = new TextBox
        {
            Header = "Stack name", Text = stack.Name, SelectionStart = 0, SelectionLength = stack.Name.Length
        };
        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = "Rename stack",
            Content = nameBox,
            PrimaryButtonText = "Rename",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };

        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(nameBox.Text))
            {
                return;
            }

            args.Cancel = true;
            nameBox.Focus(FocusState.Programmatic);
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return false;
        }

        stack.Rename(nameBox.Text);
        return true;
    }

    public static async Task<bool> ConfirmDeleteAsync(XamlRoot xamlRoot, DropStackViewModel stack)
    {
        ArgumentNullException.ThrowIfNull(xamlRoot);
        ArgumentNullException.ThrowIfNull(stack);

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = $"Delete “{stack.Name}”?",
            Content = stack.Model.Items.Count == 0
                ? "This empty stack will be removed."
                : $"This removes the stack and its {stack.ItemCountText}. Original files and folders are never deleted.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    public static Task<bool> ConfirmDeleteAsync(Window owner, DropStackViewModel stack)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(stack);

        return StackDialogWindow.ShowAsync(
            owner,
            $"Delete “{stack.Name}”?",
            stack.Model.Items.Count == 0
                ? "This empty stack will be removed."
                : $"This removes the stack and its {stack.ItemCountText}. Original files and folders are never deleted.",
            "Delete");
    }

    public static async Task<bool> ConfirmRemoveItemsAsync(
        XamlRoot xamlRoot,
        DropStackViewModel stack,
        int itemCount)
    {
        ArgumentNullException.ThrowIfNull(xamlRoot);
        ArgumentNullException.ThrowIfNull(stack);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(itemCount);

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = itemCount == 1
                ? $"Remove item from “{stack.Name}”?"
                : $"Remove {itemCount} items from “{stack.Name}”?",
            Content = itemCount == 1
                ? "This removes the item from this stack. Original files and folders are never deleted."
                : "This removes the items from this stack. Original files and folders are never deleted.",
            PrimaryButtonText = "Remove",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    public static Task<bool> ConfirmRemoveItemsAsync(
        Window owner,
        DropStackViewModel stack,
        int itemCount)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(stack);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(itemCount);

        return StackDialogWindow.ShowAsync(
            owner,
            itemCount == 1
                ? $"Remove item from “{stack.Name}”?"
                : $"Remove {itemCount} items from “{stack.Name}”?",
            itemCount == 1
                ? "This removes the item from this stack. Original files and folders are never deleted."
                : "This removes the items from this stack. Original files and folders are never deleted.",
            "Remove");
    }

    public static async Task<bool> ConfirmRecycleItemsAsync(XamlRoot xamlRoot, int itemCount)
    {
        ArgumentNullException.ThrowIfNull(xamlRoot);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(itemCount);

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = itemCount == 1 ? "Delete this item from disk?" : $"Delete {itemCount} items from disk?",
            Content = itemCount == 1
                ? "The original file or folder will be sent to the Windows Recycle Bin and removed from this stack."
                : "The original files and folders will be sent to the Windows Recycle Bin and removed from this stack.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    public static Task<bool> ConfirmRecycleItemsAsync(Window owner, int itemCount)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(itemCount);

        return StackDialogWindow.ShowAsync(
            owner,
            itemCount == 1 ? "Delete this item from disk?" : $"Delete {itemCount} items from disk?",
            itemCount == 1
                ? "The original file or folder will be sent to the Windows Recycle Bin and removed from this stack."
                : "The original files and folders will be sent to the Windows Recycle Bin and removed from this stack.",
            "Delete");
    }

    public static async Task<bool> ConfirmClearAsync(XamlRoot xamlRoot, int stackCount)
    {
        ArgumentNullException.ThrowIfNull(xamlRoot);

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = "Delete all stacks?",
            Content
                = $"This removes {stackCount} {(stackCount == 1 ? "stack" : "stacks")} and all app-owned captures. Original files and folders are never deleted.",
            PrimaryButtonText = "Delete all",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
