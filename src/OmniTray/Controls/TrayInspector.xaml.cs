// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

using System.Collections.Specialized;
using Windows.System;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace OmniTray.Controls;

public sealed partial class TrayInspector : UserControl
{
    private TrayInspectorMode _mode;
    private bool _isDisposed;

    public TrayInspector(DropStackViewModel viewModel, Window dialogOwner)
    {
        this.ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        ArgumentNullException.ThrowIfNull(dialogOwner);
        this.InitializeComponent();
        this.InspectorOrganizer.DialogOwner = dialogOwner;
        App.Current.StackCatalogViewModel.Stacks.CollectionChanged += this.OnCatalogStacksChanged;
    }

    public DropStackViewModel ViewModel { get; }

    internal Brush SurfaceBackground
    {
        set => this.InspectorSurface.Background = value;
    }

    internal event EventHandler? CloseRequested;

    internal event EventHandler? DeleteRequested;

    internal void Open(TrayInspectorMode mode)
    {
        this._mode = mode;
        this.UpdateStackCommands();
        this.ApplyMode();
    }

    internal void Dispose()
    {
        if (this._isDisposed)
        {
            return;
        }

        this._isDisposed = true;
        App.Current.StackCatalogViewModel.Stacks.CollectionChanged -= this.OnCatalogStacksChanged;
    }

    private void ApplyMode()
    {
        this.RenamePanel.Visibility = Visibility.Collapsed;
        this.RenameTitleButton.Visibility = Visibility.Visible;
        this.ColorPanel.Visibility = Visibility.Collapsed;
        this.CombinePanel.Visibility = Visibility.Collapsed;
        this.InspectorOrganizer.Visibility = Visibility.Visible;
        this.InspectorCommandBar.Visibility = Visibility.Visible;

        if (this._mode == TrayInspectorMode.Rename)
        {
            this.RenameBox.Text = this.ViewModel.Name;
            this.RenameBox.SelectionStart = 0;
            this.RenameBox.SelectionLength = this.RenameBox.Text.Length;
            this.RenameTitleButton.Visibility = Visibility.Collapsed;
            this.RenamePanel.Visibility = Visibility.Visible;
            _ = this.RenameBox.DispatcherQueue.TryEnqueue(() =>
                this.RenameBox.Focus(FocusState.Programmatic));
        }
        else if (this._mode == TrayInspectorMode.Color)
        {
            this.InspectorOrganizer.Visibility = Visibility.Collapsed;
            this.InspectorCommandBar.Visibility = Visibility.Collapsed;
            this.ColorPanel.Visibility = Visibility.Visible;
            this.InspectorColorPicker.PrepareForDisplay();
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

    private void OnCloseClick(object sender, RoutedEventArgs args) =>
        this.CloseRequested?.Invoke(this, EventArgs.Empty);

    private void OnRenameClick(object sender, RoutedEventArgs args) =>
        this.Open(TrayInspectorMode.Rename);

    private void OnColorClick(object sender, RoutedEventArgs args) =>
        this.Open(TrayInspectorMode.Color);

    private void OnDeleteClick(object sender, RoutedEventArgs args) =>
        this.DeleteRequested?.Invoke(this, EventArgs.Empty);

    private void OnCombineStacksClick(object sender, RoutedEventArgs args) =>
        this.Open(TrayInspectorMode.Combine);

    private void OnSaveRenameClick(object sender, RoutedEventArgs args) => this.SaveRename();

    private void OnColorSelected(object? sender, EventArgs args) => this.CancelInlineAction();

    private void OnRenameBoxKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key == VirtualKey.Enter)
        {
            args.Handled = true;
            this.SaveRename();
        }
        else if (args.Key == VirtualKey.Escape)
        {
            args.Handled = true;
            this.CancelInlineAction();
        }
    }

    private void SaveRename()
    {
        if (string.IsNullOrWhiteSpace(this.RenameBox.Text))
        {
            this.RenameBox.Focus(FocusState.Programmatic);
            return;
        }

        this.ViewModel.Rename(this.RenameBox.Text);
        this.CancelInlineAction();
    }

    private void OnCancelInlineActionClick(object sender, RoutedEventArgs args) =>
        this.CancelInlineAction();

    private void CancelInlineAction()
    {
        this._mode = TrayInspectorMode.Browse;
        this.RenamePanel.Visibility = Visibility.Collapsed;
        this.RenameTitleButton.Visibility = Visibility.Visible;
        this.ColorPanel.Visibility = Visibility.Collapsed;
        this.CombinePanel.Visibility = Visibility.Collapsed;
        this.InspectorOrganizer.Visibility = Visibility.Visible;
        this.InspectorCommandBar.Visibility = Visibility.Visible;
        this.CombineTargetBox.ItemsSource = null;
        this.InspectorOrganizer.Focus(FocusState.Programmatic);
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
        this.CombineStacksButton.IsEnabled = App.Current.StackCatalogViewModel.Stacks.Any(
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
