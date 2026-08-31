// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using Windows.Storage.Pickers;
using WinRT.Interop;

namespace OmniTray.Services;

internal static class VirtualStackDialogService
{
    public static async Task<DropStack?> CreateAsync(Window owner, XamlRoot xamlRoot)
    {
        var result = await EditAsync(owner, xamlRoot, null);
        return result is null
            ? null
            : DropStack.CreateVirtual(result.Name, result.Source);
    }

    public static async Task<bool> ConfigureAsync(
        Window owner,
        XamlRoot xamlRoot,
        DropStackViewModel stack)
    {
        ArgumentNullException.ThrowIfNull(stack);
        if (stack.Model.VirtualSource is null)
        {
            return false;
        }

        var result = await EditAsync(owner, xamlRoot, stack);
        if (result is null)
        {
            return false;
        }

        stack.ChangeVirtualSource(result.Source);
        if (!string.Equals(stack.Name, result.Name, StringComparison.Ordinal))
        {
            stack.Rename(result.Name);
        }

        await App.Current.RefreshVirtualStackAsync(stack);
        return true;
    }

    private static async Task<VirtualStackDialogResult?> EditAsync(
        Window owner,
        XamlRoot xamlRoot,
        DropStackViewModel? stack)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(xamlRoot);

        var currentSource = stack?.Model.VirtualSource;
        var definitions = App.Current.VirtualStacks.Definitions.ToList();
        if (currentSource is not null &&
            definitions.All(definition => !string.Equals(
                definition.Id,
                currentSource.ProviderId,
                StringComparison.Ordinal)))
        {
            definitions.Insert(0, new(
                currentSource.ProviderId,
                $"{currentSource.ProviderId} (unavailable)",
                stack!.Name,
                currentSource.Capabilities));
        }

        var nameBox = new TextBox
        {
            Header = "Stack name",
            Text = stack?.Name ?? definitions[0].DefaultStackName
        };
        var selectedSourceIndex = definitions.FindIndex(definition =>
            string.Equals(definition.Id, currentSource?.ProviderId, StringComparison.Ordinal));
        var sourceBox = new ComboBox
        {
            Header = "Source",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        // Runtime DisplayMemberPath lookup has no XAML metadata for this dialog-only model.
        foreach (var definition in definitions)
        {
            sourceBox.Items.Add(definition.DisplayName);
        }

        sourceBox.SelectedIndex = selectedSourceIndex >= 0 ? selectedSourceIndex : 0;
        var folderBox = new TextBox
        {
            Header = "Folder",
            PlaceholderText = "Choose an existing folder",
            Text = currentSource?.Configuration ?? string.Empty
        };
        var browseButton = new Button
        {
            Content = "Browse…",
            HorizontalAlignment = HorizontalAlignment.Left
        };
        browseButton.Click += async (_, _) =>
        {
            var picker = new FolderPicker
            {
                SuggestedStartLocation = PickerLocationId.ComputerFolder
            };
            picker.FileTypeFilter.Add("*");
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(owner));
            if (await picker.PickSingleFolderAsync() is { } folder)
            {
                folderBox.Text = folder.Path;
            }
        };

        var folderPanel = new StackPanel { Spacing = 8 };
        folderPanel.Children.Add(folderBox);
        folderPanel.Children.Add(browseButton);
        var capabilitiesText = new TextBlock
        {
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[
                "TextFillColorSecondaryBrush"],
            TextWrapping = TextWrapping.Wrap
        };
        var errorText = new TextBlock
        {
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[
                "SystemFillColorCriticalBrush"],
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed
        };
        var content = new StackPanel { Spacing = 12, MinWidth = 360 };
        content.Children.Add(nameBox);
        content.Children.Add(sourceBox);
        content.Children.Add(folderPanel);
        content.Children.Add(capabilitiesText);
        content.Children.Add(errorText);

        void UpdateSource(bool updateName)
        {
            if (sourceBox.SelectedIndex < 0 || sourceBox.SelectedIndex >= definitions.Count)
            {
                return;
            }

            var definition = definitions[sourceBox.SelectedIndex];

            folderPanel.Visibility = definition.RequiresFolder
                ? Visibility.Visible
                : Visibility.Collapsed;
            capabilitiesText.Text = DescribeCapabilities(definition.Capabilities);
            if (updateName)
            {
                nameBox.Text = definition.DefaultStackName;
            }
        }

        sourceBox.SelectionChanged += (_, _) => UpdateSource(updateName: stack is null);
        UpdateSource(updateName: false);

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = stack is null ? "New virtual stack" : "Configure virtual stack",
            Content = content,
            PrimaryButtonText = stack is null ? "Create" : "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };

        VirtualStackSource? source = null;
        dialog.PrimaryButtonClick += (_, args) =>
        {
            try
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(nameBox.Text);
                var definition = definitions[sourceBox.SelectedIndex];
                source = App.Current.VirtualStacks.CreateSource(
                    definition.Id,
                    definition.RequiresFolder ? folderBox.Text : null);
                errorText.Visibility = Visibility.Collapsed;
            }
            catch (Exception exception)
            {
                args.Cancel = true;
                errorText.Text = exception.Message;
                errorText.Visibility = Visibility.Visible;
            }
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary && source is not null
            ? new(nameBox.Text.Trim(), source)
            : null;
    }

    private static string DescribeCapabilities(VirtualStackCapabilities capabilities)
    {
        var operations = new List<string>();
        if ((capabilities & VirtualStackCapabilities.Read) != 0)
        {
            operations.Add("shows items");
        }

        if ((capabilities & VirtualStackCapabilities.Write) != 0)
        {
            operations.Add("accepts items");
        }

        if ((capabilities & VirtualStackCapabilities.Remove) != 0)
        {
            operations.Add("can remove source items");
        }

        return $"Capabilities: {string.Join(", ", operations)}.";
    }

    private sealed record VirtualStackDialogResult(string Name, VirtualStackSource Source);
}
