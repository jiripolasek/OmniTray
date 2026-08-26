// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

using System.ComponentModel;
using Windows.UI;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using OmniTray.Controls;

namespace OmniTray.Views;

internal enum TrayInspectorPlacement
{
    Top,
    Right,
    Bottom,
    Left
}

internal sealed class TrayInspectorPopup : IDisposable
{
    private readonly TrayInspector _content;
    private readonly Window _dialogOwner;
    private readonly Popup _popup;
    private readonly DropStackViewModel _viewModel;
    private Action? _closePreparationCompleted;
    private bool _isDisposed;
    private bool _isDeleteDialogOpen;
    private bool _isPopupClosing;
    private bool _isPopupOpen;
    private bool _isPreparingForClose;
    private TrayInspectorMode _mode;
    private Action? _pendingPopupAction;

    public TrayInspectorPopup(
        Window dialogOwner,
        Popup popup,
        FrameworkElement placementTarget,
        DropStackViewModel viewModel,
        TrayInspectorPlacement preferredPlacement)
    {
        this._dialogOwner = dialogOwner ?? throw new ArgumentNullException(nameof(dialogOwner));
        this._popup = popup ?? throw new ArgumentNullException(nameof(popup));
        this._popup.PlacementTarget = placementTarget ?? throw new ArgumentNullException(nameof(placementTarget));
        this._popup.DesiredPlacement = ToPopupPlacementMode(preferredPlacement);
        this._viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        this._content = new TrayInspector(viewModel, dialogOwner);
        this._popup.Child = this._content;
        this._popup.Opened += this.OnPopupOpened;
        this._popup.Closed += this.OnPopupClosed;
        this._content.DeleteRequested += this.OnDeleteRequested;
        this._content.ActualThemeChanged += this.OnActualThemeChanged;
        this._viewModel.PropertyChanged += this.OnViewModelPropertyChanged;
        this.ApplyBackdrop();
    }

    public void Show(TrayInspectorMode mode)
    {
        if (this._isDisposed || this._isPreparingForClose)
        {
            return;
        }

        this._mode = mode;
        if (!this._popup.IsOpen)
        {
            this._popup.IsOpen = true;
            return;
        }

        this._content.Open(mode);
    }

    public void PrepareForClose(Action completed)
    {
        ArgumentNullException.ThrowIfNull(completed);
        if (this._isPreparingForClose)
        {
            return;
        }

        this._isPreparingForClose = true;
        this._pendingPopupAction = null;
        this._closePreparationCompleted = completed;
        if (this._popup.IsOpen)
        {
            this._isPopupClosing = true;
            this._popup.IsOpen = false;
        }
        else if (this._isPopupOpen)
        {
            this._isPopupClosing = true;
        }

        this.TryCompleteClosePreparation();
    }

    public async Task ConfirmDeleteAsync()
    {
        if (this._isDisposed || this._isPreparingForClose || this._isDeleteDialogOpen)
        {
            return;
        }

        this._isDeleteDialogOpen = true;
        try
        {
            if (!await StackDialogService.ConfirmDeleteAsync(this._dialogOwner, this._viewModel) ||
                this._isDisposed ||
                !App.Current.StackCatalogViewModel.Stacks.Contains(this._viewModel))
            {
                return;
            }

            this.RequestAfterPopupClosed(this.DeleteStackAfterPopupClosed);
        }
        finally
        {
            this._isDeleteDialogOpen = false;
        }
    }

    public void Dispose()
    {
        if (this._isDisposed)
        {
            return;
        }

        this._isDisposed = true;
        this._pendingPopupAction = null;
        this._closePreparationCompleted = null;
        this._popup.Opened -= this.OnPopupOpened;
        this._popup.Closed -= this.OnPopupClosed;
        this._content.DeleteRequested -= this.OnDeleteRequested;
        this._content.ActualThemeChanged -= this.OnActualThemeChanged;
        this._viewModel.PropertyChanged -= this.OnViewModelPropertyChanged;
        if (ReferenceEquals(this._popup.Child, this._content))
        {
            this._popup.Child = null;
        }

        this._content.Dispose();
    }

    private void OnPopupOpened(object? sender, object args)
    {
        this._isPopupOpen = true;
        this._isPopupClosing = false;
        this._content.Open(this._mode);
    }

    private static PopupPlacementMode ToPopupPlacementMode(TrayInspectorPlacement placement) =>
        placement switch
        {
            TrayInspectorPlacement.Top => PopupPlacementMode.Top,
            TrayInspectorPlacement.Right => PopupPlacementMode.Right,
            TrayInspectorPlacement.Bottom => PopupPlacementMode.Bottom,
            TrayInspectorPlacement.Left => PopupPlacementMode.Left,
            _ => throw new ArgumentOutOfRangeException(nameof(placement), placement, null)
        };

    private void OnPopupClosed(object? sender, object args)
    {
        this._content.OnPopupClosed();
        this._isPopupOpen = false;
        this._isPopupClosing = false;
        if (this._isPreparingForClose)
        {
            this._pendingPopupAction = null;
            this.TryCompleteClosePreparation();
            return;
        }

        var action = this._pendingPopupAction;
        this._pendingPopupAction = null;
        this.EnqueueAfterPopupClosed(action);
    }

    private void OnDeleteRequested(object? sender, EventArgs args) =>
        _ = this.ConfirmDeleteAsync();

    private async void DeleteStackAfterPopupClosed() =>
        await App.Current.DeleteStackAsync(this._viewModel);

    private void RequestAfterPopupClosed(Action action)
    {
        if (this._isPreparingForClose || this._isDisposed)
        {
            return;
        }

        this._pendingPopupAction = action;
        if (!this._popup.IsOpen && !this._isPopupOpen && !this._isPopupClosing)
        {
            this._pendingPopupAction = null;
            this.EnqueueAfterPopupClosed(action);
            return;
        }

        if (this._popup.IsOpen)
        {
            this._isPopupClosing = true;
            this._popup.IsOpen = false;
        }
    }

    private void EnqueueAfterPopupClosed(Action? action)
    {
        if (action is null)
        {
            return;
        }

        this._popup.DispatcherQueue.TryEnqueue(
            DispatcherQueuePriority.Low,
            () =>
            {
                if (!this._isPreparingForClose && !this._isDisposed)
                {
                    action();
                }
            });
    }

    private void TryCompleteClosePreparation()
    {
        if (!this._isPreparingForClose ||
            this._closePreparationCompleted is not { } completed ||
            this._isPopupOpen ||
            this._isPopupClosing ||
            this._popup.IsOpen)
        {
            return;
        }

        this._closePreparationCompleted = null;
        if (!this._popup.DispatcherQueue.TryEnqueue(
                DispatcherQueuePriority.Low,
                () => completed()))
        {
            completed();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(DropStackViewModel.Tint))
        {
            this.ApplyBackdrop();
        }
        else if (args.PropertyName == nameof(DropStackViewModel.TintColor))
        {
            var usesUntintedBackdrop = StackTintPalette.IsNeutral(this._viewModel.Tint) &&
                                       !StackTintPalette.UseSystemAccentForNeutral;
            if (!usesUntintedBackdrop && this._content.SurfaceBackdrop is TintedAcrylicBackdrop tintedBackdrop)
            {
                tintedBackdrop.TintColor = this._viewModel.TintColor;
            }
            else
            {
                this.ApplyBackdrop();
            }
        }
    }

    private void OnActualThemeChanged(FrameworkElement sender, object args)
    {
        if (!TintedAcrylicBackdrop.IsSupported)
        {
            this.ApplyFallbackBackground();
        }
    }

    private void ApplyBackdrop()
    {
        if (!TintedAcrylicBackdrop.IsSupported)
        {
            this._content.SurfaceBackdrop = null;
            this.ApplyFallbackBackground();
            return;
        }

        this._content.SurfaceBackground = new SolidColorBrush(Colors.Transparent);
        if (StackTintPalette.IsNeutral(this._viewModel.Tint) &&
            !StackTintPalette.UseSystemAccentForNeutral)
        {
            if (this._content.SurfaceBackdrop is not DesktopAcrylicBackdrop)
            {
                this._content.SurfaceBackdrop = new DesktopAcrylicBackdrop();
            }

            return;
        }

        if (this._content.SurfaceBackdrop is TintedAcrylicBackdrop tintedBackdrop)
        {
            tintedBackdrop.TintColor = this._viewModel.TintColor;
            return;
        }

        this._content.SurfaceBackdrop = new TintedAcrylicBackdrop(this._viewModel.TintColor);
    }

    private void ApplyFallbackBackground() =>
        this._content.SurfaceBackground = this.CreateFallbackBackground();

    private SolidColorBrush CreateFallbackBackground()
    {
        if (!StackTintPalette.IsNeutral(this._viewModel.Tint) ||
            StackTintPalette.UseSystemAccentForNeutral)
        {
            return new SolidColorBrush(
                TintedAcrylicBackdrop.CreateFallbackColor(
                    this._viewModel.TintColor,
                    this._content.ActualTheme));
        }

        if (Application.Current.Resources.TryGetValue("SolidBackgroundFillColorBaseBrush", out var resource) &&
            resource is SolidColorBrush brush)
        {
            return new SolidColorBrush(brush.Color);
        }

        var isDark = this._content.ActualTheme == ElementTheme.Dark ||
                     (this._content.ActualTheme == ElementTheme.Default &&
                      Application.Current.RequestedTheme == ApplicationTheme.Dark);
        return new SolidColorBrush(
            isDark
                ? Color.FromArgb(byte.MaxValue, 0x20, 0x20, 0x20)
                : Color.FromArgb(byte.MaxValue, 0xF3, 0xF3, 0xF3));
    }
}
