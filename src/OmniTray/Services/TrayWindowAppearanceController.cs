// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.ComponentModel;
using Windows.UI;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;

namespace OmniTray.Services;

internal sealed class TrayWindowAppearanceController : IDisposable
{
    private readonly Window _window;
    private readonly Panel _root;
    private readonly TrayContentViewModel _viewModel;
    private bool _isDisposed;

    public TrayWindowAppearanceController(
        Window window,
        Panel root,
        TrayContentViewModel viewModel)
    {
        this._window = window ?? throw new ArgumentNullException(nameof(window));
        this._root = root ?? throw new ArgumentNullException(nameof(root));
        this._viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        this._viewModel.PropertyChanged += this.OnViewModelPropertyChanged;
        this._root.ActualThemeChanged += this.OnActualThemeChanged;
        this._window.Title = this._viewModel.Name;
        this.ApplyBackdrop();
    }

    public void Dispose()
    {
        if (this._isDisposed)
        {
            return;
        }

        this._isDisposed = true;
        this._viewModel.PropertyChanged -= this.OnViewModelPropertyChanged;
        this._root.ActualThemeChanged -= this.OnActualThemeChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (string.IsNullOrEmpty(args.PropertyName) ||
            args.PropertyName == nameof(TrayContentViewModel.Name))
        {
            this._window.Title = this._viewModel.Name;
        }

        if (string.IsNullOrEmpty(args.PropertyName) ||
            args.PropertyName == nameof(TrayContentViewModel.Tint))
        {
            this.ApplyBackdrop();
        }
        else if (args.PropertyName == nameof(TrayContentViewModel.TintColor))
        {
            if (this._window.SystemBackdrop is TintedAcrylicBackdrop backdrop)
            {
                backdrop.TintColor = this._viewModel.TintColor;
            }
            else if (!TintedAcrylicBackdrop.IsSupported)
            {
                this.ApplyFallbackBackground();
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
            this.ApplyFallbackBackground();
            return;
        }

        this._root.Background = new SolidColorBrush(Colors.Transparent);
        this._window.SystemBackdrop = StackTintPalette.IsNeutral(this._viewModel.Tint)
            ? new DesktopAcrylicBackdrop()
            : new TintedAcrylicBackdrop(this._viewModel.TintColor);
    }

    private void ApplyFallbackBackground() =>
        this._root.Background = this.CreateFallbackBackground();

    private SolidColorBrush CreateFallbackBackground()
    {
        if (!StackTintPalette.IsNeutral(this._viewModel.Tint))
        {
            return new SolidColorBrush(
                TintedAcrylicBackdrop.CreateFallbackColor(
                    this._viewModel.TintColor,
                    this._root.ActualTheme));
        }

        if (Application.Current.Resources.TryGetValue("SolidBackgroundFillColorBaseBrush", out var resource) &&
            resource is SolidColorBrush brush)
        {
            return new SolidColorBrush(brush.Color);
        }

        var isDark = this._root.ActualTheme == ElementTheme.Dark ||
                     (this._root.ActualTheme == ElementTheme.Default &&
                      Application.Current.RequestedTheme == ApplicationTheme.Dark);
        return new SolidColorBrush(
            isDark
                ? Color.FromArgb(byte.MaxValue, 0x20, 0x20, 0x20)
                : Color.FromArgb(byte.MaxValue, 0xF3, 0xF3, 0xF3));
    }
}
