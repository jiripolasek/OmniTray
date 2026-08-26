// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

using System.Collections.Specialized;
using System.ComponentModel;
using Windows.System;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace OmniTray.Controls;

public sealed partial class TrayInspector : UserControl
{
    private TrayInspectorMode _mode;
    private string? _customizeOriginalTint;
    private bool _isDisposed;
    private bool _isSynchronizingViewSelection;

    public TrayInspector(DropStackViewModel viewModel, Window dialogOwner)
    {
        this.ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        ArgumentNullException.ThrowIfNull(dialogOwner);
        this.InitializeComponent();
        this.ViewModel.PropertyChanged += this.OnViewModelPropertyChanged;
        this.RestoreViewSelection();
        this.InspectorViewSelector.SelectionChanged += this.OnViewSelectionChanged;
        this.InspectorOrganizer.DialogOwner = dialogOwner;
        App.Current.StackCatalogViewModel.Stacks.CollectionChanged += this.OnCatalogStacksChanged;
    }

    public DropStackViewModel ViewModel { get; }

    internal Brush SurfaceBackground
    {
        set => this.InspectorSurface.Background = value;
    }

    internal SystemBackdrop? SurfaceBackdrop
    {
        get => this.InspectorBackdrop.SystemBackdrop;
        set => this.InspectorBackdrop.SystemBackdrop = value;
    }

    internal Brush SurfaceTint
    {
        set => this.InspectorTintOverlay.Background = value;
    }

    internal event EventHandler? DeleteRequested;

    internal void Open(TrayInspectorMode mode)
    {
        if (this._mode == TrayInspectorMode.Customize && mode != TrayInspectorMode.Customize)
        {
            this.RevertPendingCustomization();
        }

        this._mode = mode;
        this.UpdateStackCommands();
        this.ApplyMode();
    }

    internal void OnPopupClosed()
    {
        this.RevertPendingCustomization();
        this.CombineTargetBox.ItemsSource = null;
    }

    internal void Dispose()
    {
        if (this._isDisposed)
        {
            return;
        }

        this._isDisposed = true;
        this.InspectorViewSelector.SelectionChanged -= this.OnViewSelectionChanged;
        this.ViewModel.PropertyChanged -= this.OnViewModelPropertyChanged;
        App.Current.StackCatalogViewModel.Stacks.CollectionChanged -= this.OnCatalogStacksChanged;
    }

    private void ApplyMode()
    {
        this.RestoreViewSelection();
        this.BrowseHeader.Visibility = Visibility.Visible;
        this.CustomizeHeader.Visibility = Visibility.Collapsed;
        this.CustomizePanel.Visibility = Visibility.Collapsed;
        this.CombinePanel.Visibility = Visibility.Collapsed;
        this.InspectorOrganizer.Visibility = Visibility.Visible;

        if (this._mode == TrayInspectorMode.Customize)
        {
            this._customizeOriginalTint = this.ViewModel.Tint;
            this.CustomizeNameBox.Text = this.ViewModel.Name;
            this.CustomizeNameBox.SelectionStart = 0;
            this.CustomizeNameBox.SelectionLength = this.CustomizeNameBox.Text.Length;
            this.BrowseHeader.Visibility = Visibility.Collapsed;
            this.CustomizeHeader.Visibility = Visibility.Visible;
            this.CustomizePanel.Visibility = Visibility.Visible;
            this.InspectorOrganizer.Visibility = Visibility.Collapsed;
            this.InspectorColorPicker.PrepareForDisplay();
            _ = this.CustomizeNameBox.DispatcherQueue.TryEnqueue(() =>
                this.CustomizeNameBox.Focus(FocusState.Programmatic));
        }
        else if (this._mode == TrayInspectorMode.Combine)
        {
            if (!this.UpdateCombineTargets())
            {
                this.CancelInlineAction();
                return;
            }

            this.CombinePanel.Visibility = Visibility.Visible;
            _ = this.CombineTargetBox.DispatcherQueue.TryEnqueue(() =>
                this.CombineTargetBox.Focus(FocusState.Programmatic));
        }
    }

    private void OnCustomizeClick(object sender, RoutedEventArgs args) =>
        this.Open(TrayInspectorMode.Customize);

    private void OnRenameTitlePointerChanged(object sender, PointerRoutedEventArgs args) =>
        this.UpdateRenameEditIconVisibility();

    private void OnRenameTitleFocusChanged(object sender, RoutedEventArgs args) =>
        this.UpdateRenameEditIconVisibility();

    private void UpdateRenameEditIconVisibility() =>
        this.RenameEditIcon.Opacity = this.RenameTitleButton.IsPointerOver ||
            this.RenameTitleButton.FocusState != FocusState.Unfocused
                ? 1
                : 0;

    private void OnDeleteClick(object sender, RoutedEventArgs args) =>
        this.DeleteRequested?.Invoke(this, EventArgs.Empty);

    private void OnCombineStacksClick(object sender, RoutedEventArgs args) =>
        this.Open(TrayInspectorMode.Combine);

    private void OnSaveCustomizeClick(object sender, RoutedEventArgs args) => this.SaveCustomization();

    private void OnViewSelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        var viewMode = this.InspectorViewSelector.SelectedIndex == 1
            ? StackInspectorViewMode.Grid
            : StackInspectorViewMode.List;
        this.InspectorOrganizer.SetThumbnailView(viewMode == StackInspectorViewMode.Grid);
        if (!this._isSynchronizingViewSelection && viewMode != this.ViewModel.InspectorViewMode)
        {
            this.ViewModel.ChangeInspectorViewMode(viewMode);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(DropStackViewModel.InspectorViewMode))
        {
            this.RestoreViewSelection();
        }
    }

    private void RestoreViewSelection()
    {
        var selectedIndex = this.ViewModel.InspectorViewMode == StackInspectorViewMode.Grid ? 1 : 0;
        this._isSynchronizingViewSelection = true;
        try
        {
            this.InspectorViewSelector.SelectedIndex = selectedIndex;
        }
        finally
        {
            this._isSynchronizingViewSelection = false;
        }

        this.InspectorOrganizer.SetThumbnailView(selectedIndex == 1);
    }

    private void OnCustomizeNameBoxKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key == VirtualKey.Enter)
        {
            args.Handled = true;
            this.SaveCustomization();
        }
        else if (args.Key == VirtualKey.Escape)
        {
            args.Handled = true;
            this.CancelInlineAction();
        }
    }

    private void SaveCustomization()
    {
        if (string.IsNullOrWhiteSpace(this.CustomizeNameBox.Text))
        {
            this.CustomizeNameBox.Focus(FocusState.Programmatic);
            return;
        }

        this.ViewModel.Rename(this.CustomizeNameBox.Text);
        this._customizeOriginalTint = null;
        this.ReturnToBrowse();
    }

    private void OnCancelInlineActionClick(object sender, RoutedEventArgs args) =>
        this.CancelInlineAction();

    private void CancelInlineAction()
    {
        this.RevertPendingCustomization();
        this.ReturnToBrowse();
    }

    private void ReturnToBrowse()
    {
        this._mode = TrayInspectorMode.Browse;
        this.CombineTargetBox.ItemsSource = null;
        this.ApplyMode();
        this.InspectorOrganizer.Focus(FocusState.Programmatic);
    }

    private void RevertPendingCustomization()
    {
        if (this._customizeOriginalTint is not { } originalTint)
        {
            return;
        }

        this._customizeOriginalTint = null;
        if (!string.Equals(this.ViewModel.Tint, originalTint, StringComparison.OrdinalIgnoreCase))
        {
            this.ViewModel.ChangeTint(originalTint);
        }
    }

    private void OnConfirmCombineClick(object sender, RoutedEventArgs args)
    {
        if (this.CombineTargetBox.SelectedItem is not DropStackViewModel source ||
            !App.Current.CombineStacks(this.ViewModel, source))
        {
            return;
        }

        this.CancelInlineAction();
        this.UpdateStackCommands();
    }

    private void OnCatalogStacksChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        this.UpdateStackCommands();
        if (this._mode == TrayInspectorMode.Combine && !this.UpdateCombineTargets())
        {
            this.CancelInlineAction();
        }
    }

    private void UpdateStackCommands() =>
        this.CombineStacksMenuItem.IsEnabled = App.Current.StackCatalogViewModel.Stacks.Any(
            stack => !ReferenceEquals(stack, this.ViewModel));

    private bool UpdateCombineTargets()
    {
        var selectedStackId = (this.CombineTargetBox.SelectedItem as DropStackViewModel)?.Model.Id;
        var choices = App.Current.StackCatalogViewModel.Stacks
            .Where(stack => !ReferenceEquals(stack, this.ViewModel))
            .ToArray();
        this.CombineTargetBox.ItemsSource = choices;
        this.CombineTargetBox.SelectedItem = choices.FirstOrDefault(
            stack => stack.Model.Id == selectedStackId) ?? choices.FirstOrDefault();
        return choices.Length > 0;
    }
}
