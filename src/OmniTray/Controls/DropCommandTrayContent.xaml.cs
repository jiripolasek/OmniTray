// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using Windows.ApplicationModel.DataTransfer;

namespace OmniTray.Controls;

internal sealed partial class DropCommandTrayContent : UserControl, ITrayWindowContent
{
    private readonly Window _owner;
    private Action? _closePreparationCompleted;
    private bool _isDisposed;
    private bool _isPreparingForClose;
    private bool _isStackPickerOpen;
    private MenuFlyout? _stackPicker;

    public DropCommandTrayContent(Window owner, DropCommandViewModel viewModel, bool isMinimal)
    {
        this._owner = owner ?? throw new ArgumentNullException(nameof(owner));
        this.ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        this.IsMinimal = isMinimal;
        this.InitializeComponent();
        this.NormalContent.Visibility = isMinimal ? Visibility.Collapsed : Visibility.Visible;
        this.MinimalContent.Visibility = isMinimal ? Visibility.Visible : Visibility.Collapsed;
    }

    public DropCommandViewModel ViewModel { get; }

    public bool IsMinimal { get; }

    public FrameworkElement View => this;

    public IReadOnlyList<TrayContextAction> ContextActions { get; } = [];

    public void PrepareForClose(Action completed)
    {
        ArgumentNullException.ThrowIfNull(completed);
        if (this._isPreparingForClose)
        {
            return;
        }

        this._isPreparingForClose = true;
        this._closePreparationCompleted = completed;
        if (this._isStackPickerOpen)
        {
            this._stackPicker?.Hide();
            return;
        }

        this.CompleteClosePreparation();
    }

    public void Dispose()
    {
        if (this._isDisposed)
        {
            return;
        }

        this._isDisposed = true;
        this._closePreparationCompleted = null;
        this.DetachStackPicker();
    }

    private void OnDragOver(object sender, DragEventArgs args)
    {
        args.Handled = true;
        var accepted = App.Current.CanPotentiallyExecuteDropCommand(this.ViewModel.Id, args.DataView);
        args.AcceptedOperation = accepted ? DataPackageOperation.Copy : DataPackageOperation.None;
        args.DragUIOverride.Caption = accepted
            ? this.ViewModel.Name
            : this.ViewModel.AcceptanceText;
        args.DragUIOverride.IsCaptionVisible = true;
        args.DragUIOverride.IsContentVisible = true;
        this.SetDropHintVisible(accepted);
    }

    private void OnDragLeave(object sender, DragEventArgs args)
    {
        args.Handled = true;
        this.SetDropHintVisible(false);
    }

    private async void OnDrop(object sender, DragEventArgs args)
    {
        args.Handled = true;
        this.SetDropHintVisible(false);
        await App.Current.ExecuteDropCommandAsync(this.ViewModel.Id, args.DataView, this._owner);
    }

    private void OnChooseStackClick(object sender, RoutedEventArgs args)
    {
        if (this._isPreparingForClose || sender is not FrameworkElement anchor)
        {
            return;
        }

        this.DetachStackPicker();
        var flyout = new MenuFlyout();
        foreach (var stack in App.Current.StackCatalogViewModel.Stacks)
        {
            var item = new MenuFlyoutItem
            {
                Text = stack.Name,
                IsEnabled = stack.Model.Items.Count > 0,
                Icon = new FontIcon { Glyph = stack.LeadingGlyph }
            };
            item.Click += async (_, _) =>
                await App.Current.ExecuteDropCommandAsync(this.ViewModel.Id, stack, this._owner);
            flyout.Items.Add(item);
        }

        if (flyout.Items.Count == 0)
        {
            flyout.Items.Add(new MenuFlyoutItem { Text = "No stacks available", IsEnabled = false });
        }

        this._stackPicker = flyout;
        flyout.Opened += this.OnStackPickerOpened;
        flyout.Closed += this.OnStackPickerClosed;
        flyout.ShowAt(anchor);
    }

    private void OnStackPickerOpened(object? sender, object args) => this._isStackPickerOpen = true;

    private void OnStackPickerClosed(object? sender, object args)
    {
        this._isStackPickerOpen = false;
        this.DetachStackPicker();
        if (this._isPreparingForClose)
        {
            this.CompleteClosePreparation();
        }
    }

    private void DetachStackPicker()
    {
        if (this._stackPicker is not { } flyout)
        {
            return;
        }

        flyout.Opened -= this.OnStackPickerOpened;
        flyout.Closed -= this.OnStackPickerClosed;
        this._stackPicker = null;
        this._isStackPickerOpen = false;
    }

    private void CompleteClosePreparation()
    {
        var completed = this._closePreparationCompleted;
        this._closePreparationCompleted = null;
        completed?.Invoke();
    }

    private void SetDropHintVisible(bool isVisible)
    {
        this.DropOutline.Visibility = this.IsMinimal && isVisible
            ? Visibility.Visible
            : Visibility.Collapsed;
        this.DropHintOverlay.Visibility = !this.IsMinimal && isVisible
            ? Visibility.Visible
            : Visibility.Collapsed;
    }
}
