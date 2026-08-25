// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

using System.Diagnostics;
using Windows.ApplicationModel;
using Windows.System;
using Microsoft.UI;
using Microsoft.UI.Windowing;

namespace OmniTray.Views;

public sealed partial class SettingsWindow : Window
{
    private readonly StartupTaskService _startupTaskService = new();
    private bool _isUpdatingStartupTask;

    public SettingsWindow()
    {
        this.ViewModel = new SettingsViewModel(
            App.Current.StackCatalogViewModel,
            App.Current.DropCommandCatalogViewModel);
        this.InitializeComponent();
        this.PopulateCommandTemplateFlyout();

        this.ExtendsContentIntoTitleBar = true;
        this.SetTitleBar(this.AppTitleBar);
        if (AppWindowTitleBar.IsCustomizationSupported())
        {
            this.AppWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
            this.AppWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        }

        this.AllowMoveOnDragOutToggle.IsOn = App.Current.AllowMoveOnDragOutPreference;
        this.ToastPositionBox.SelectedIndex = (int)App.Current.ToastPositionPreference;
        this.CommandSurfaceBox.SelectedIndex = 0;
        this.Closed += (_, _) => this.ViewModel.Dispose();
        this.UpdateToastSystemSettingsLink();
    }

    public SettingsViewModel ViewModel { get; }

    public string VersionText { get; } = GetVersionText();

    public Visibility VisibleWhen(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

    public Visibility CollapsedWhen(bool value) => value ? Visibility.Collapsed : Visibility.Visible;

    private async void OnLoaded(object sender, RoutedEventArgs args)
    {
        this.SettingsNavigation.SelectedItem = this.GeneralNavigationItem;
        await this.RefreshStartupTaskAsync();
    }

    private void OnSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var tag = (args.SelectedItem as NavigationViewItem)?.Tag as string ?? "general";
        this.GeneralSection.Visibility = tag == "general" ? Visibility.Visible : Visibility.Collapsed;
        this.TraysSection.Visibility = tag == "trays" ? Visibility.Visible : Visibility.Collapsed;
        this.CommandsSection.Visibility = tag == "commands" ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void OnAddCommandTemplateClick(object sender, RoutedEventArgs args)
    {
        if ((sender as FrameworkElement)?.Tag is not string templateId ||
            !DropCommandTemplates.TryGet(templateId, out _))
        {
            this.ShowCommandStatus("That command template is not available.", InfoBarSeverity.Warning);
            return;
        }

        var initial = this.ViewModel.CreateCommand(templateId);
        var result = await DropCommandEditorDialogService.ShowAsync(
            this,
            this.SettingsNavigation.XamlRoot,
            this.ViewModel,
            initial,
            this.ViewModel.CommandSurfaceId,
            true);
        if (result is null)
        {
            return;
        }

        this.ViewModel.AddCommand(result.Command, result.SurfaceIds);
        this.ShowCommandStatus($"Added “{result.Command.DisplayName}”.", InfoBarSeverity.Success);
    }

    private void PopulateCommandTemplateFlyout()
    {
        foreach (var template in DropCommandTemplates.All)
        {
            var item = new MenuFlyoutItem
            {
                Tag = template.Id,
                Text = template.DisplayName
            };
            item.Click += this.OnAddCommandTemplateClick;
            this.AddCommandTemplateFlyout.Items.Add(item);
        }
    }

    private async void OnEditCommandClick(object sender, RoutedEventArgs args)
    {
        if ((sender as FrameworkElement)?.DataContext is not DropCommandViewModel command)
        {
            return;
        }

        var result = await DropCommandEditorDialogService.ShowAsync(
            this,
            this.SettingsNavigation.XamlRoot,
            this.ViewModel,
            command.Model,
            this.ViewModel.CommandSurfaceId,
            false);
        if (result is null)
        {
            return;
        }

        this.ViewModel.UpdateCommand(result.Command, result.SurfaceIds);
        this.ShowCommandStatus($"Saved “{result.Command.DisplayName}”.", InfoBarSeverity.Success);
    }

    private void OnPopOutCommandClick(object sender, RoutedEventArgs args)
    {
        if ((sender as FrameworkElement)?.DataContext is DropCommandViewModel command)
        {
            App.Current.OpenDropCommand(command);
        }
    }

    private async void OnDeleteCommandClick(object sender, RoutedEventArgs args)
    {
        if ((sender as FrameworkElement)?.DataContext is not DropCommandViewModel command)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = this.SettingsNavigation.XamlRoot,
            Title = $"Delete “{command.Name}”?",
            Content = "This removes the configured command from every surface. It does not change any files or folders.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        _ = this.ViewModel.RemoveCommand(command.Id);
        this.ShowCommandStatus($"Deleted “{command.Name}”.", InfoBarSeverity.Success);
    }

    private void OnCommandSurfaceSelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if ((this.CommandSurfaceBox.SelectedItem as FrameworkElement)?.Tag is string surfaceId)
        {
            this.ViewModel.SetCommandSurface(surfaceId);
        }
    }

    private async void OnAddExistingCommandClick(object sender, RoutedEventArgs args)
    {
        var candidates = this.ViewModel.GetCommandsNotOnCurrentSurface();
        if (candidates.Count == 0)
        {
            this.ShowCommandStatus(
                this.ViewModel.CommandDefinitions.Count == 0
                    ? "Configure a command first."
                    : "Every configured command is already on this surface.",
                InfoBarSeverity.Informational);
            return;
        }

        var commandBox = new ComboBox
        {
            Header = "Command",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        // Runtime DisplayMemberPath lookup has no XAML metadata for these dialog-only models.
        foreach (var candidate in candidates)
        {
            commandBox.Items.Add(candidate.Name);
        }

        commandBox.SelectedIndex = 0;
        var dialog = new ContentDialog
        {
            XamlRoot = this.SettingsNavigation.XamlRoot,
            Title = "Add existing command",
            Content = commandBox,
            PrimaryButtonText = "Add",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary ||
            commandBox.SelectedIndex < 0 ||
            commandBox.SelectedIndex >= candidates.Count)
        {
            return;
        }

        var command = candidates[commandBox.SelectedIndex];
        _ = this.ViewModel.AddCommandToCurrentSurface(command.Id);
        this.ShowCommandStatus($"Added “{command.Name}” to this surface.", InfoBarSeverity.Success);
    }

    private async void OnAddCommandFolderClick(object sender, RoutedEventArgs args)
    {
        var nameBox = new TextBox
        {
            Header = "Folder name",
            PlaceholderText = "Utilities"
        };
        var dialog = new ContentDialog
        {
            XamlRoot = this.SettingsNavigation.XamlRoot,
            Title = "Add command folder",
            Content = nameBox,
            PrimaryButtonText = "Add",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };
        dialog.PrimaryButtonClick += (_, eventArgs) =>
        {
            if (!string.IsNullOrWhiteSpace(nameBox.Text))
            {
                return;
            }

            eventArgs.Cancel = true;
            nameBox.Focus(FocusState.Programmatic);
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        _ = this.ViewModel.AddRootFolder(nameBox.Text);
        this.ShowCommandStatus($"Added folder “{nameBox.Text.Trim()}”.", InfoBarSeverity.Success);
    }

    private void OnMoveCommandPlacementUpClick(object sender, RoutedEventArgs args) =>
        this.MoveCommandPlacement(sender, -1);

    private void OnMoveCommandPlacementDownClick(object sender, RoutedEventArgs args) =>
        this.MoveCommandPlacement(sender, 1);

    private void MoveCommandPlacement(object sender, int direction)
    {
        if ((sender as FrameworkElement)?.DataContext is DropCommandPlacementViewModel placement)
        {
            _ = this.ViewModel.MovePlacement(placement.NodeId, direction);
        }
    }

    private async void OnRenameCommandFolderClick(object sender, RoutedEventArgs args)
    {
        if ((sender as FrameworkElement)?.DataContext is not DropCommandPlacementViewModel { IsFolder: true } folder)
        {
            return;
        }

        var nameBox = new TextBox
        {
            Header = "Folder name",
            Text = folder.DisplayName,
            SelectionStart = 0,
            SelectionLength = folder.DisplayName.Length
        };
        var dialog = new ContentDialog
        {
            XamlRoot = this.SettingsNavigation.XamlRoot,
            Title = $"Rename “{folder.DisplayName}”",
            Content = nameBox,
            PrimaryButtonText = "Rename",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };
        dialog.PrimaryButtonClick += (_, eventArgs) =>
        {
            if (!string.IsNullOrWhiteSpace(nameBox.Text))
            {
                return;
            }

            eventArgs.Cancel = true;
            nameBox.Focus(FocusState.Programmatic);
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            _ = this.ViewModel.RenameFolder(folder.NodeId, nameBox.Text);
        }
    }

    private async void OnMoveCommandPlacementToFolderClick(object sender, RoutedEventArgs args)
    {
        if ((sender as FrameworkElement)?.DataContext is not DropCommandPlacementViewModel placement)
        {
            return;
        }

        var options = this.ViewModel.GetParentFolderOptions(placement);

        var folderBox = new ComboBox
        {
            Header = "Parent folder",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        // Keep the displayed strings and typed options paired by index for the same reason.
        foreach (var option in options)
        {
            folderBox.Items.Add(option.DisplayName);
        }

        folderBox.SelectedIndex = Math.Max(0, options.ToList().FindIndex(option => option.Id == placement.ParentId));
        var dialog = new ContentDialog
        {
            XamlRoot = this.SettingsNavigation.XamlRoot,
            Title = $"Move “{placement.DisplayName}”",
            Content = folderBox,
            PrimaryButtonText = "Move",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary ||
            folderBox.SelectedIndex < 0 ||
            folderBox.SelectedIndex >= options.Count)
        {
            return;
        }

        var parent = options[folderBox.SelectedIndex];
        _ = this.ViewModel.SetPlacementParent(placement.NodeId, parent.Id);
    }

    private async void OnRemoveCommandPlacementClick(object sender, RoutedEventArgs args)
    {
        if ((sender as FrameworkElement)?.DataContext is not DropCommandPlacementViewModel placement)
        {
            return;
        }

        if (placement.IsFolder)
        {
            var dialog = new ContentDialog
            {
                XamlRoot = this.SettingsNavigation.XamlRoot,
                Title = $"Remove folder “{placement.DisplayName}”?",
                Content = "Commands and nested folders inside it will move up one level.",
                PrimaryButtonText = "Remove folder",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }
        }

        _ = this.ViewModel.RemovePlacement(placement.NodeId);
    }

    private void ShowCommandStatus(string message, InfoBarSeverity severity)
    {
        this.CommandStatusBar.Message = message;
        this.CommandStatusBar.Severity = severity;
        this.CommandStatusBar.IsOpen = true;
    }

    private void OnAllowMoveOnDragOutToggled(object sender, RoutedEventArgs args) =>
        App.Current.AllowMoveOnDragOutPreference = this.AllowMoveOnDragOutToggle.IsOn;

    private void OnToastPositionSelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (this.ToastPositionBox.SelectedIndex is < 0 or > 3)
        {
            return;
        }

        App.Current.ToastPositionPreference = (ToastPosition)this.ToastPositionBox.SelectedIndex;
        this.UpdateToastSystemSettingsLink();
    }

    private void UpdateToastSystemSettingsLink() =>
        this.ToastSystemSettingsLink.Visibility
            = this.ToastPositionBox.SelectedIndex == (int)ToastPosition.UseSystemSettings
                ? Visibility.Visible
                : Visibility.Collapsed;

    private async void OnOpenSystemNotificationSettingsClick(
        object sender,
        RoutedEventArgs args) =>
        await Launcher.LaunchUriAsync(new Uri("ms-settings:notifications"));

    private async Task RefreshStartupTaskAsync()
    {
        try
        {
            this.ApplyStartupTaskState(await this._startupTaskService.GetStateAsync());
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"OmniTray could not read its startup registration: {exception}");
            this.ShowStartupTaskUnavailable();
        }
    }

    private async void OnStartWithWindowsToggled(object sender, RoutedEventArgs args)
    {
        if (this._isUpdatingStartupTask)
        {
            return;
        }

        this.StartWithWindowsToggle.IsEnabled = false;
        try
        {
            var state = await this._startupTaskService.SetEnabledAsync(this.StartWithWindowsToggle.IsOn);
            this.ApplyStartupTaskState(state);
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"OmniTray could not update its startup registration: {exception}");
            this.ShowStartupTaskUnavailable();
        }
    }

    private void ApplyStartupTaskState(StartupTaskState state)
    {
        this._isUpdatingStartupTask = true;
        try
        {
            switch (state)
            {
                case StartupTaskState.Enabled:
                    this.SetStartupTaskControls(
                        true,
                        true,
                        "OmniTray starts quietly in the notification area when you sign in.",
                        false);
                    break;

                case StartupTaskState.Disabled:
                    this.SetStartupTaskControls(
                        false,
                        true,
                        "OmniTray does not start when you sign in.",
                        false);
                    break;

                case StartupTaskState.DisabledByUser:
                    this.SetStartupTaskControls(
                        false,
                        false,
                        "Windows disabled startup for OmniTray. Re-enable it in Startup Apps.",
                        true);
                    break;

                case StartupTaskState.DisabledByPolicy:
                    this.SetStartupTaskControls(
                        false,
                        false,
                        "Startup is disabled by your organization's policy.",
                        true);
                    break;

                case StartupTaskState.EnabledByPolicy:
                    this.SetStartupTaskControls(
                        true,
                        false,
                        "Startup is enabled by your organization's policy.",
                        false);
                    break;
            }
        }
        finally
        {
            this._isUpdatingStartupTask = false;
        }
    }

    private void SetStartupTaskControls(
        bool isOn,
        bool isEnabled,
        string description,
        bool showSettingsLink)
    {
        this.StartWithWindowsToggle.IsOn = isOn;
        this.StartWithWindowsToggle.IsEnabled = isEnabled;
        this.StartWithWindowsDescription.Text = description;
        this.StartupAppsSettingsLink.Visibility = showSettingsLink
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void ShowStartupTaskUnavailable()
    {
        this._isUpdatingStartupTask = true;
        try
        {
            this.SetStartupTaskControls(
                false,
                false,
                "Startup registration is unavailable for this installation.",
                true);
        }
        finally
        {
            this._isUpdatingStartupTask = false;
        }
    }

    private async void OnOpenStartupAppsSettingsClick(object sender, RoutedEventArgs args) =>
        await Launcher.LaunchUriAsync(new Uri("ms-settings:startupapps"));

    private static string GetVersionText()
    {
        var version = Package.Current.Id.Version;
        return version.Revision == 0
            ? $"{version.Major}.{version.Minor}.{version.Build}"
            : $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
    }
}
