// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using Windows.Storage.Pickers;
using WinRT.Interop;

namespace OmniTray.Services;

internal sealed record DropCommandEditorResult(
    DropCommandInstance Command,
    IReadOnlyList<string> SurfaceIds);

internal static class DropCommandEditorDialogService
{
    public static async Task<DropCommandEditorResult?> ShowAsync(
        Window owner,
        XamlRoot xamlRoot,
        SettingsViewModel settings,
        DropCommandInstance initial,
        string defaultSurfaceId,
        bool isNew)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(xamlRoot);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(initial);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultSurfaceId);

        var template = DropCommandTemplates.Get(initial.TemplateId);
        var parameters = initial.Parameters.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value,
            StringComparer.Ordinal);
        var nameBox = new TextBox
        {
            Header = "Name",
            Text = initial.DisplayName,
            SelectionStart = 0,
            SelectionLength = initial.DisplayName.Length
        };
        var enabledSwitch = new ToggleSwitch
        {
            Header = "Availability",
            OnContent = "Enabled",
            OffContent = "Disabled",
            IsOn = initial.IsEnabled
        };
        var errorText = new TextBlock
        {
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"],
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed
        };
        var content = new StackPanel { Spacing = 14 };

        if (template is null)
        {
            content.Children.Add(new InfoBar
            {
                IsOpen = true,
                IsClosable = false,
                Severity = InfoBarSeverity.Warning,
                Title = "Template unavailable",
                Message = $"The template “{initial.TemplateId}” is not installed. Its ID and parameters will be preserved."
            });
        }
        else
        {
            content.Children.Add(new TextBlock
            {
                Text = template.Description,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[
                    "TextFillColorSecondaryBrush"],
                TextWrapping = TextWrapping.Wrap
            });
        }

        content.Children.Add(nameBox);
        content.Children.Add(enabledSwitch);

        ComboBox? applicationTargetBox = null;
        StackPanel? desktopApplicationPanel = null;
        StackPanel? packagedApplicationPanel = null;
        TextBox? executableBox = null;
        TextBox? extraArgumentsBox = null;
        ComboBox? packagedAppBox = null;
        TextBox? appUserModelIdBox = null;
        if (template?.ConfiguresApplication == true)
        {
            applicationTargetBox = new ComboBox
            {
                Header = "Application type",
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            var desktopTarget = new ComboBoxItem
            {
                Content = "Desktop application",
                Tag = DropCommandApplicationTargetIds.DesktopExecutable
            };
            var packagedTarget = new ComboBoxItem
            {
                Content = "Packaged application",
                Tag = DropCommandApplicationTargetIds.PackagedApp
            };
            applicationTargetBox.Items.Add(desktopTarget);
            applicationTargetBox.Items.Add(packagedTarget);

            var targetId = DropCommandTemplates.GetApplicationTargetId(initial);
            applicationTargetBox.SelectedItem = targetId switch
            {
                DropCommandApplicationTargetIds.DesktopExecutable => desktopTarget,
                DropCommandApplicationTargetIds.PackagedApp => packagedTarget,
                _ => CreateUnavailableTarget(applicationTargetBox, targetId)
            };
            content.Children.Add(applicationTargetBox);

            executableBox = new TextBox
            {
                Header = "Application",
                PlaceholderText = "Choose an executable",
                Text = parameters.GetValueOrDefault(DropCommandParameterNames.ExecutablePath, string.Empty)
            };
            var executableBrowseButton = new Button
            {
                Content = "Browse…",
                HorizontalAlignment = HorizontalAlignment.Left
            };
            executableBrowseButton.Click += async (_, _) =>
            {
                var picker = new FileOpenPicker
                {
                    SuggestedStartLocation = PickerLocationId.ComputerFolder
                };
                picker.FileTypeFilter.Add(".exe");
                InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(owner));
                if (await picker.PickSingleFileAsync() is { } file)
                {
                    executableBox.Text = file.Path;
                }
            };
            extraArgumentsBox = new TextBox
            {
                Header = "Extra arguments",
                Description = "Optional. Enter one argument per line; dropped paths are appended.",
                AcceptsReturn = true,
                MinHeight = 86,
                Text = parameters.GetValueOrDefault(DropCommandParameterNames.ExtraArguments, string.Empty),
                TextWrapping = TextWrapping.NoWrap
            };
            desktopApplicationPanel = new StackPanel { Spacing = 10 };
            desktopApplicationPanel.Children.Add(executableBox);
            desktopApplicationPanel.Children.Add(executableBrowseButton);
            desktopApplicationPanel.Children.Add(extraArgumentsBox);
            content.Children.Add(desktopApplicationPanel);

            IReadOnlyList<PackagedAppDescriptor> installedApps = [];
            string? appDiscoveryError = null;
            try
            {
                installedApps = await PackagedAppService.GetInstalledAppsAsync();
            }
            catch (Exception exception)
            {
                appDiscoveryError = exception.Message;
            }

            var savedAppUserModelId = parameters.GetValueOrDefault(
                DropCommandParameterNames.AppUserModelId,
                string.Empty);
            var packagedApps = installedApps.ToList();
            if (!string.IsNullOrWhiteSpace(savedAppUserModelId) &&
                packagedApps.All(app => !StringComparer.OrdinalIgnoreCase.Equals(
                    app.AppUserModelId,
                    savedAppUserModelId)))
            {
                var savedDisplayName = parameters.GetValueOrDefault(
                    DropCommandParameterNames.PackagedAppDisplayName,
                    savedAppUserModelId);
                packagedApps.Add(new PackagedAppDescriptor(savedDisplayName, savedAppUserModelId));
            }

            packagedApps = packagedApps
                .OrderBy(static app => app.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(static app => app.AppUserModelId, StringComparer.OrdinalIgnoreCase)
                .ToList();
            packagedAppBox = new ComboBox
            {
                Header = "Packaged application",
                PlaceholderText = "Choose an installed app",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                MaxDropDownHeight = 420
            };
            // Runtime DisplayMemberPath lookup has no XAML metadata for this dialog-only model.
            foreach (var app in packagedApps)
            {
                packagedAppBox.Items.Add(app.DisplayName);
            }

            appUserModelIdBox = new TextBox
            {
                Header = "Application user model ID",
                Description = "Filled by the picker. You can also paste an AUMID for an app that is not listed.",
                PlaceholderText = "PackageFamilyName!ApplicationId",
                Text = savedAppUserModelId
            };
            packagedAppBox.SelectedIndex = packagedApps.FindIndex(app =>
                StringComparer.OrdinalIgnoreCase.Equals(app.AppUserModelId, savedAppUserModelId));
            packagedAppBox.SelectionChanged += (_, _) =>
            {
                if (packagedAppBox.SelectedIndex >= 0 &&
                    packagedAppBox.SelectedIndex < packagedApps.Count)
                {
                    var app = packagedApps[packagedAppBox.SelectedIndex];
                    appUserModelIdBox.Text = app.AppUserModelId;
                }
            };

            packagedApplicationPanel = new StackPanel { Spacing = 10 };
            if (appDiscoveryError is not null)
            {
                packagedApplicationPanel.Children.Add(new InfoBar
                {
                    IsOpen = true,
                    IsClosable = false,
                    Severity = InfoBarSeverity.Warning,
                    Title = "Installed apps could not be listed",
                    Message = $"You can still paste an application user model ID below. {appDiscoveryError}"
                });
            }
            else if (installedApps.Count == 0)
            {
                packagedApplicationPanel.Children.Add(new InfoBar
                {
                    IsOpen = true,
                    IsClosable = false,
                    Severity = InfoBarSeverity.Informational,
                    Title = "No packaged apps found",
                    Message = "Paste an application user model ID below to configure this target manually."
                });
            }

            packagedApplicationPanel.Children.Add(packagedAppBox);
            packagedApplicationPanel.Children.Add(new TextBlock
            {
                Text = "The app must be registered to handle the dropped file type. Packaged targets use Windows file activation and do not receive extra arguments.",
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[
                    "TextFillColorSecondaryBrush"],
                TextWrapping = TextWrapping.Wrap
            });
            packagedApplicationPanel.Children.Add(appUserModelIdBox);
            content.Children.Add(packagedApplicationPanel);
        }

        TextBox? destinationBox = null;
        if (template?.RequiresDestinationFolder == true)
        {
            destinationBox = new TextBox
            {
                Header = "Destination folder",
                PlaceholderText = "Choose a folder",
                Text = parameters.GetValueOrDefault(DropCommandParameterNames.DestinationFolder, string.Empty)
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
                    destinationBox.Text = folder.Path;
                }
            };
            content.Children.Add(destinationBox);
            content.Children.Add(browseButton);
        }

        var acceptedKindsText = new TextBlock
        {
            Text = DropCommandTemplates.GetAcceptanceText(initial),
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[
                "TextFillColorSecondaryBrush"],
            TextWrapping = TextWrapping.Wrap
        };
        var acceptedKindsPanel = new StackPanel { Spacing = 6 };
        acceptedKindsPanel.Children.Add(new TextBlock
        {
            Text = "Accepted content",
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"]
        });
        acceptedKindsPanel.Children.Add(acceptedKindsText);
        content.Children.Add(acceptedKindsPanel);

        if (applicationTargetBox is not null &&
            desktopApplicationPanel is not null &&
            packagedApplicationPanel is not null)
        {
            void UpdateApplicationTarget()
            {
                var selectedTargetId = GetSelectedApplicationTargetId(applicationTargetBox);
                parameters[DropCommandParameterNames.ApplicationTarget] = selectedTargetId;
                desktopApplicationPanel.Visibility =
                    selectedTargetId == DropCommandApplicationTargetIds.DesktopExecutable
                        ? Visibility.Visible
                        : Visibility.Collapsed;
                packagedApplicationPanel.Visibility =
                    selectedTargetId == DropCommandApplicationTargetIds.PackagedApp
                        ? Visibility.Visible
                        : Visibility.Collapsed;
                acceptedKindsText.Text = DropCommandTemplates.GetAcceptanceText(
                    initial.Reconfigure(initial.DisplayName, parameters, initial.IsEnabled));
            }

            applicationTargetBox.SelectionChanged += (_, _) => UpdateApplicationTarget();
            UpdateApplicationTarget();
        }

        var surfacesPanel = new StackPanel { Spacing = 6 };
        surfacesPanel.Children.Add(new TextBlock
        {
            Text = "Show on",
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"]
        });
        var surfaceChecks = settings.GetCommandSurfaceIds().Select(surfaceId => new KeyValuePair<string, CheckBox>(
            surfaceId,
            new CheckBox
            {
                Content = GetSurfaceLabel(surfaceId),
                IsChecked = isNew
                    ? StringComparer.Ordinal.Equals(surfaceId, defaultSurfaceId)
                    : settings.HasPlacement(initial.Id, surfaceId)
            })).ToArray();
        foreach (var (_, checkBox) in surfaceChecks)
        {
            surfacesPanel.Children.Add(checkBox);
        }

        content.Children.Add(surfacesPanel);
        content.Children.Add(errorText);

        var scrollViewer = new ScrollViewer
        {
            Content = content,
            MaxHeight = 590,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = isNew ? $"Add {template?.DisplayName ?? "command"}" : $"Edit “{initial.DisplayName}”",
            Content = scrollViewer,
            PrimaryButtonText = isNew ? "Add" : "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            var validationError = Validate(
                nameBox.Text,
                template,
                applicationTargetBox is null ? null : GetSelectedApplicationTargetId(applicationTargetBox),
                executableBox?.Text,
                appUserModelIdBox?.Text,
                destinationBox?.Text);
            if (validationError is null)
            {
                return;
            }

            args.Cancel = true;
            errorText.Text = validationError;
            errorText.Visibility = Visibility.Visible;
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return null;
        }

        if (applicationTargetBox is not null)
        {
            parameters[DropCommandParameterNames.ApplicationTarget] =
                GetSelectedApplicationTargetId(applicationTargetBox);
        }

        if (executableBox is not null)
        {
            parameters[DropCommandParameterNames.ExecutablePath] = executableBox.Text.Trim();
        }

        if (extraArgumentsBox is not null)
        {
            parameters[DropCommandParameterNames.ExtraArguments] = extraArgumentsBox.Text.Trim();
        }

        if (appUserModelIdBox is not null)
        {
            var appUserModelId = appUserModelIdBox.Text.Trim();
            parameters[DropCommandParameterNames.AppUserModelId] = appUserModelId;
            parameters[DropCommandParameterNames.PackagedAppDisplayName] =
                packagedAppBox?.SelectedItem is PackagedAppDescriptor selectedApp &&
                StringComparer.OrdinalIgnoreCase.Equals(selectedApp.AppUserModelId, appUserModelId)
                    ? selectedApp.DisplayName
                    : appUserModelId;
        }

        if (destinationBox is not null)
        {
            parameters[DropCommandParameterNames.DestinationFolder] = destinationBox.Text.Trim();
        }

        var selectedSurfaces = surfaceChecks
            .Where(static pair => pair.Value.IsChecked == true)
            .Select(static pair => pair.Key)
            .ToArray();
        return new DropCommandEditorResult(
            initial.Reconfigure(nameBox.Text, parameters, enabledSwitch.IsOn),
            selectedSurfaces);
    }

    private static string? Validate(
        string displayName,
        DropCommandTemplateDescriptor? template,
        string? applicationTargetId,
        string? executable,
        string? appUserModelId,
        string? destination)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return "Enter a command name.";
        }

        if (template?.ConfiguresApplication == true)
        {
            if (applicationTargetId == DropCommandApplicationTargetIds.DesktopExecutable &&
                (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable.Trim())))
            {
                return "Choose a desktop application that exists.";
            }

            if (applicationTargetId == DropCommandApplicationTargetIds.PackagedApp &&
                !DropCommandTemplates.IsPackagedAppUserModelId(appUserModelId))
            {
                return "Choose a packaged application or enter a valid application user model ID.";
            }

            if (applicationTargetId is not DropCommandApplicationTargetIds.DesktopExecutable and
                not DropCommandApplicationTargetIds.PackagedApp)
            {
                return "Choose a supported application type.";
            }
        }

        if (template?.RequiresDestinationFolder == true &&
            (string.IsNullOrWhiteSpace(destination) || !Directory.Exists(destination.Trim())))
        {
            return "Choose a destination folder that exists.";
        }

        return null;
    }

    private static ComboBoxItem CreateUnavailableTarget(ComboBox targetBox, string targetId)
    {
        var unavailableTarget = new ComboBoxItem
        {
            Content = $"Unavailable application type ({targetId})",
            Tag = targetId,
            IsEnabled = false
        };
        targetBox.Items.Add(unavailableTarget);
        return unavailableTarget;
    }

    private static string GetSelectedApplicationTargetId(ComboBox targetBox) =>
        (targetBox.SelectedItem as ComboBoxItem)?.Tag as string ?? string.Empty;

    private static string GetSurfaceLabel(string surfaceId) => surfaceId switch
    {
        DropCommandSurfaceIds.Popup => "Popup",
        DropCommandSurfaceIds.LeftEdge => "Left edge",
        DropCommandSurfaceIds.RightEdge => "Right edge",
        DropCommandSurfaceIds.TopEdge => "Top edge",
        DropCommandSurfaceIds.BottomEdge => "Bottom edge",
        _ => surfaceId
    };
}
