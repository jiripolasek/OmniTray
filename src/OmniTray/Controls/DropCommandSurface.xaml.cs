// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Collections.ObjectModel;
using Windows.ApplicationModel.DataTransfer;
using Microsoft.UI.Xaml.Media;

namespace OmniTray.Controls;

public sealed partial class DropCommandSurface : UserControl
{
    private readonly DispatcherTimer _folderDwellTimer = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private readonly Stack<(Guid? FolderId, string Name)> _navigation = [];
    private DropCommandPlacementViewModel? _dwellFolder;
    private bool _hasBackDwellTriggered;
    private bool _isBackDwellActive;
    private bool _isSubscribed;
    private bool _showRootHeader = true;
    private string _surfaceId = DropCommandSurfaceIds.Popup;

    public DropCommandSurface()
    {
        this.InitializeComponent();
        this._folderDwellTimer.Tick += this.OnFolderDwellTick;
        this.Loaded += this.OnLoaded;
        this.Unloaded += this.OnUnloaded;
    }

    public ObservableCollection<DropCommandPlacementViewModel> Items { get; } = [];

    public Window? OwnerWindow { get; set; }

    public bool HasContent { get; private set; }

    public bool ShowRootHeader
    {
        get => this._showRootHeader;
        set
        {
            this._showRootHeader = value;
            this.UpdateHeaderVisibility();
        }
    }

    public double VerticalItemsMaxHeight
    {
        get => this.VerticalItems.MaxHeight;
        set => this.VerticalItems.MaxHeight = value;
    }

    public string SurfaceId
    {
        get => this._surfaceId;
        set
        {
            this._surfaceId = string.IsNullOrWhiteSpace(value) ? DropCommandSurfaceIds.Popup : value;
            this._navigation.Clear();
            this.Refresh();
        }
    }

    public bool IsHorizontal
    {
        get => this.HorizontalLayout.Visibility == Visibility.Visible;
        set
        {
            this.HorizontalLayout.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            this.VerticalItems.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
            this.UpdateHeaderVisibility();
        }
    }

    public event EventHandler? ContentAvailabilityChanged;

    public event EventHandler? ExternalDragEntered;

    public event EventHandler? ExternalDragLeft;

    public event EventHandler? CommandDropCompleted;

    internal void ResetNavigation()
    {
        this.StopFolderDwell();
        this._hasBackDwellTriggered = false;
        this.HorizontalBackDropOutline.Visibility = Visibility.Collapsed;
        if (this._navigation.Count == 0)
        {
            return;
        }

        this._navigation.Clear();
        this.Refresh();
    }

    private Guid? CurrentFolderId => this._navigation.Count == 0 ? null : this._navigation.Peek().FolderId;

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (!this._isSubscribed)
        {
            App.Current.DropCommandCatalogViewModel.CatalogChanged += this.OnCatalogChanged;
            this._isSubscribed = true;
        }

        this.Refresh();
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        if (this._isSubscribed)
        {
            App.Current.DropCommandCatalogViewModel.CatalogChanged -= this.OnCatalogChanged;
            this._isSubscribed = false;
        }

        this.StopFolderDwell();
        this._hasBackDwellTriggered = false;
    }

    private void OnCatalogChanged(object? sender, EventArgs args) => this.Refresh();

    private void Refresh()
    {
        if (Application.Current is not App app)
        {
            return;
        }

        while (this._navigation.Count > 0)
        {
            var current = this._navigation.Peek();
            var folder = current.FolderId is { } folderId
                ? app.DropCommandCatalogViewModel.FindFolder(this.SurfaceId, folderId)
                : null;
            if (folder is null)
            {
                _ = this._navigation.Pop();
                continue;
            }

            if (!StringComparer.Ordinal.Equals(current.Name, folder.Name))
            {
                _ = this._navigation.Pop();
                this._navigation.Push((folder.Id, folder.Name));
            }

            break;
        }

        var items = app.DropCommandCatalogViewModel.GetChildren(this.SurfaceId, this.CurrentFolderId);
        this.Items.Clear();
        foreach (var item in items)
        {
            this.Items.Add(item);
        }

        var hasContent = this.Items.Count > 0 || this._navigation.Count > 0;
        this.RootGrid.Visibility = hasContent ? Visibility.Visible : Visibility.Collapsed;
        if (this.HasContent != hasContent)
        {
            this.HasContent = hasContent;
            this.ContentAvailabilityChanged?.Invoke(this, EventArgs.Empty);
        }

        var backButtonVisibility = this._navigation.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        this.BackButton.Visibility = backButtonVisibility;
        this.HorizontalBackTarget.Visibility = backButtonVisibility;
        this.HeaderText.Text = this._navigation.Count > 0 ? this._navigation.Peek().Name : "Commands";
        this.UpdateHeaderVisibility();
    }

    private void UpdateHeaderVisibility()
    {
        this.HeaderGrid.Visibility = this.IsHorizontal || (!this.ShowRootHeader && this._navigation.Count == 0)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void OnNodeClick(object sender, RoutedEventArgs args)
    {
        if ((sender as FrameworkElement)?.Tag is not DropCommandPlacementViewModel node)
        {
            return;
        }

        if (node.IsFolder)
        {
            this.OpenFolder(node);
            return;
        }

        if (node.Command is { } command && sender is FrameworkElement anchor)
        {
            this.ShowStackPicker(anchor, command);
        }
    }

    private void OnPopOutClick(object sender, RoutedEventArgs args)
    {
        if ((sender as FrameworkElement)?.Tag is DropCommandPlacementViewModel { Command: { } command })
        {
            App.Current.OpenDropCommand(command);
        }
    }

    private void OnBackClick(object sender, RoutedEventArgs args)
    {
        this._hasBackDwellTriggered = false;
        this.NavigateBack();
    }

    private void OnBackDragEnter(object sender, DragEventArgs args)
    {
        args.Handled = true;
        this.StopFolderDwell();
        this._hasBackDwellTriggered = false;
    }

    private void OnBackDragOver(object sender, DragEventArgs args)
    {
        args.Handled = true;
        this.ExternalDragEntered?.Invoke(this, EventArgs.Empty);
        var accepted = DragDropDataService.HasActiveDrag || DragDropDataService.HasSupportedFormat(args.DataView);
        args.AcceptedOperation = accepted ? DataPackageOperation.Copy : DataPackageOperation.None;
        args.DragUIOverride.Caption = accepted ? "Back to parent command folder" : "Unsupported content";
        args.DragUIOverride.IsCaptionVisible = true;
        args.DragUIOverride.IsContentVisible = true;
        var canTrigger = accepted && !this._hasBackDwellTriggered;
        this.HorizontalBackDropOutline.Visibility = canTrigger ? Visibility.Visible : Visibility.Collapsed;
        if (canTrigger)
        {
            this.StartBackDwell();
        }
        else
        {
            this.StopFolderDwell();
        }
    }

    private void OnBackDragLeave(object sender, DragEventArgs args)
    {
        args.Handled = true;
        this._hasBackDwellTriggered = false;
        this.HorizontalBackDropOutline.Visibility = Visibility.Collapsed;
        this.StopFolderDwell();
        this.ExternalDragLeft?.Invoke(this, EventArgs.Empty);
    }

    private void OnBackDrop(object sender, DragEventArgs args)
    {
        args.Handled = true;
        args.AcceptedOperation = DataPackageOperation.None;
        this._hasBackDwellTriggered = false;
        this.HorizontalBackDropOutline.Visibility = Visibility.Collapsed;
        this.StopFolderDwell();
        this.ExternalDragLeft?.Invoke(this, EventArgs.Empty);
        this.NavigateBack();
    }

    private void OnNodeDragOver(object sender, DragEventArgs args)
    {
        args.Handled = true;
        this.ExternalDragEntered?.Invoke(this, EventArgs.Empty);
        if ((sender as FrameworkElement)?.Tag is not DropCommandPlacementViewModel node)
        {
            args.AcceptedOperation = DataPackageOperation.None;
            return;
        }

        var accepted = node.IsFolder
            ? App.Current.CanPotentiallyExecuteDropCommandFolder(this.SurfaceId, node.NodeId, args.DataView)
            : node.Command is { } command &&
              App.Current.CanPotentiallyExecuteDropCommand(command.Id, args.DataView);
        args.AcceptedOperation = accepted ? DataPackageOperation.Copy : DataPackageOperation.None;
        args.DragUIOverride.Caption = accepted
            ? node.IsFolder ? $"Open {node.DisplayName}" : node.DisplayName
            : node.IsFolder ? "No compatible commands in this folder" : node.Command?.AcceptanceText ?? "Unavailable";
        args.DragUIOverride.IsCaptionVisible = true;
        args.DragUIOverride.IsContentVisible = true;
        SetDropOutline(sender as FrameworkElement, accepted);
        if (accepted && node.IsFolder)
        {
            this.StartFolderDwell(node);
        }
        else
        {
            this.StopFolderDwell();
        }
    }

    private void OnNodeDragLeave(object sender, DragEventArgs args)
    {
        SetDropOutline(sender as FrameworkElement, false);
        this.StopFolderDwell();
        this.ExternalDragLeft?.Invoke(this, EventArgs.Empty);
    }

    private async void OnNodeDrop(object sender, DragEventArgs args)
    {
        args.Handled = true;
        SetDropOutline(sender as FrameworkElement, false);
        this.StopFolderDwell();
        this.ExternalDragLeft?.Invoke(this, EventArgs.Empty);
        if ((sender as FrameworkElement)?.Tag is not DropCommandPlacementViewModel node)
        {
            return;
        }

        if (node.IsFolder)
        {
            // A folder is navigation, not an executable drop target. If the user releases
            // before dwell navigation completes, do not report a successful copy to the source.
            args.AcceptedOperation = DataPackageOperation.None;
            this.OpenFolder(node);
            return;
        }

        if (node.Command is { } command && this.OwnerWindow is { } owner)
        {
            await App.Current.ExecuteDropCommandAsync(command.Id, args.DataView, owner);
            this.CommandDropCompleted?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OpenFolder(DropCommandPlacementViewModel folder)
    {
        if (!folder.IsFolder)
        {
            return;
        }

        this._navigation.Push((folder.NodeId, folder.DisplayName));
        this._hasBackDwellTriggered = false;
        this.Refresh();
    }

    private void NavigateBack()
    {
        this.StopFolderDwell();
        if (this._navigation.Count > 0)
        {
            _ = this._navigation.Pop();
            this.Refresh();
        }
    }

    private void StartFolderDwell(DropCommandPlacementViewModel folder)
    {
        if (!this._isBackDwellActive && ReferenceEquals(this._dwellFolder, folder))
        {
            return;
        }

        this.StopFolderDwell();
        this._dwellFolder = folder;
        this._folderDwellTimer.Start();
    }

    private void StartBackDwell()
    {
        if (this._isBackDwellActive || this._hasBackDwellTriggered)
        {
            return;
        }

        this.StopFolderDwell();
        this._isBackDwellActive = true;
        this._folderDwellTimer.Start();
    }

    private void StopFolderDwell()
    {
        this._folderDwellTimer.Stop();
        this._dwellFolder = null;
        this._isBackDwellActive = false;
    }

    private void OnFolderDwellTick(object? sender, object args)
    {
        var folder = this._dwellFolder;
        var navigateBack = this._isBackDwellActive;
        this.StopFolderDwell();
        if (navigateBack)
        {
            this._hasBackDwellTriggered = true;
            this.HorizontalBackDropOutline.Visibility = Visibility.Collapsed;
            this.NavigateBack();
        }
        else if (folder is not null)
        {
            this.OpenFolder(folder);
        }
    }

    private void ShowStackPicker(FrameworkElement anchor, DropCommandViewModel command)
    {
        if (this.OwnerWindow is not { } owner)
        {
            return;
        }

        var flyout = new MenuFlyout();
        foreach (var stack in App.Current.StackCatalogViewModel.Stacks)
        {
            var item = new MenuFlyoutItem
            {
                Text = stack.Name,
                IsEnabled = stack.Model.Items.Count > 0,
                Icon = new FontIcon { Glyph = stack.LeadingGlyph }
            };
            item.Click += async (_, _) => await App.Current.ExecuteDropCommandAsync(command.Id, stack, owner);
            flyout.Items.Add(item);
        }

        if (flyout.Items.Count == 0)
        {
            flyout.Items.Add(new MenuFlyoutItem { Text = "No stacks available", IsEnabled = false });
        }

        flyout.ShowAt(anchor);
    }

    private static void SetDropOutline(FrameworkElement? root, bool visible)
    {
        if (FindDescendant<Border>(root, "DropOutline") is { } outline)
        {
            outline.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private static T? FindDescendant<T>(DependencyObject? root, string name)
        where T : FrameworkElement
    {
        if (root is null)
        {
            return null;
        }

        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T element && element.Name == name)
            {
                return element;
            }

            if (FindDescendant<T>(child, name) is { } descendant)
            {
                return descendant;
            }
        }

        return null;
    }
}
