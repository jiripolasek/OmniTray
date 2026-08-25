// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using Windows.UI;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using OmniTray.Controls;
using WinUIEx;

namespace OmniTray.Views;

public sealed partial class TrayWindow : WindowEx
{
    internal const int DefaultWidthInDips = 196;
    internal const int DefaultHeightInDips = 242;

    private readonly TrayWindowAppearanceController _appearance;
    private readonly ITrayWindowContent _trayContent;
    private Action? _closePreparationCompleted;
    private Flyout? _colorFlyout;
    private bool _isColorFlyoutOpen;
    private bool _isContentPreparedForClose;
    private bool _isContextFlyoutOpen;
    private bool _isPreparingForClose;
    private Action? _pendingContextFlyoutAction;

    internal TrayWindow(
        TrayContentViewModel viewModel,
        TrayWindowContentFactory contentFactory)
    {
        this.ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        ArgumentNullException.ThrowIfNull(contentFactory);
        this.InitializeComponent();
        this._trayContent = contentFactory(this, false);
        this.ContentHost.Content = this._trayContent.View;
        this.AddContentActions();
        this._appearance = new TrayWindowAppearanceController(this, this.RootGrid, viewModel);

        this.IsShownInSwitchers = false;
        this.IsAlwaysOnTop = true;
        this.IsMaximizable = false;
        this.IsMinimizable = false;
        this.IsResizable = false;
        this.IsTitleBarVisible = true;
        this.ExtendsContentIntoTitleBar = true;
        this.SetTitleBar(this.AppTitleBar);
        if (AppWindowTitleBar.IsCustomizationSupported())
        {
            this.AppWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
            this.AppWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        }

        this.Closed += this.OnClosed;
    }

    internal TrayContentViewModel ViewModel { get; }

    internal event EventHandler? CloseRequested;

    internal event EventHandler? MinimalModeRequested;

    internal void PrepareForClose(Action completed)
    {
        ArgumentNullException.ThrowIfNull(completed);
        if (this._isPreparingForClose)
        {
            return;
        }

        this._isPreparingForClose = true;
        this._pendingContextFlyoutAction = null;
        this._isContentPreparedForClose = false;
        this._closePreparationCompleted = completed;
        if (this._isContextFlyoutOpen)
        {
            this.TrayContextFlyout.Hide();
        }

        if (this._isColorFlyoutOpen)
        {
            this._colorFlyout?.Hide();
        }

        this._trayContent.PrepareForClose(() =>
        {
            this._isContentPreparedForClose = true;
            this.TryCompleteClosePreparation();
        });
        this.TryCompleteClosePreparation();
    }

    private void AddContentActions()
    {
        var insertIndex = 1;
        foreach (var action in this._trayContent.ContextActions)
        {
            if (action.BeginsGroup && insertIndex > 1)
            {
                this.TrayContextFlyout.Items.Insert(insertIndex++, new MenuFlyoutSeparator());
            }

            var item = new MenuFlyoutItem
            {
                Text = action.Text,
                Icon = new SymbolIcon(action.Icon)
            };
            item.Click += (_, _) => this.RequestAfterContextFlyoutClosed(action.Execute);
            this.TrayContextFlyout.Items.Insert(insertIndex++, item);
        }

        this.ContentActionsSeparator.Visibility = this._trayContent.ContextActions.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void OnEnterMinimalModeClick(object sender, RoutedEventArgs args) =>
        this.RequestAfterContextFlyoutClosed(() =>
            this.MinimalModeRequested?.Invoke(this, EventArgs.Empty));

    private void OnColorTintMenuClick(object sender, RoutedEventArgs args) =>
        this.RequestAfterContextFlyoutClosed(this.ShowColorPalette);

    private void OnCloseTrayMenuClick(object sender, RoutedEventArgs args) =>
        this.RequestAfterContextFlyoutClosed(() =>
            this.CloseRequested?.Invoke(this, EventArgs.Empty));

    private void OnTrayContextFlyoutOpened(object? sender, object args) =>
        this._isContextFlyoutOpen = true;

    private void OnTrayContextFlyoutClosed(object? sender, object args)
    {
        this._isContextFlyoutOpen = false;
        if (this._isPreparingForClose)
        {
            this._pendingContextFlyoutAction = null;
            this.TryCompleteClosePreparation();
            return;
        }

        var action = this._pendingContextFlyoutAction;
        this._pendingContextFlyoutAction = null;
        this.EnqueueAfterContextFlyoutClosed(action);
    }

    private void RequestAfterContextFlyoutClosed(Action action)
    {
        if (this._isPreparingForClose)
        {
            return;
        }

        this._pendingContextFlyoutAction = action;
        if (!this._isContextFlyoutOpen)
        {
            this._pendingContextFlyoutAction = null;
            this.EnqueueAfterContextFlyoutClosed(action);
        }
    }

    private void EnqueueAfterContextFlyoutClosed(Action? action)
    {
        if (action is null)
        {
            return;
        }

        this.DispatcherQueue.TryEnqueue(
            DispatcherQueuePriority.Low,
            () =>
            {
                if (!this._isPreparingForClose)
                {
                    action();
                }
            });
    }

    private void ShowColorPalette()
    {
        if (this.ContentHost.XamlRoot is null)
        {
            return;
        }

        this.DetachColorFlyout();
        var flyout = TrayColorPaletteFlyout.Create(
            () => this.ViewModel.Tint,
            this.ViewModel.ChangeTint);
        this._colorFlyout = flyout;
        this._isColorFlyoutOpen = true;
        flyout.Opened += this.OnColorFlyoutOpened;
        flyout.Closed += this.OnColorFlyoutClosed;
        flyout.ShowAt(this.ContentHost);
    }

    private void OnColorFlyoutOpened(object? sender, object args) =>
        this._isColorFlyoutOpen = true;

    private void OnColorFlyoutClosed(object? sender, object args)
    {
        this._isColorFlyoutOpen = false;
        this.DetachColorFlyout();
        if (this._isPreparingForClose)
        {
            this.TryCompleteClosePreparation();
        }
    }

    private void DetachColorFlyout()
    {
        if (this._colorFlyout is not { } flyout)
        {
            return;
        }

        flyout.Opened -= this.OnColorFlyoutOpened;
        flyout.Closed -= this.OnColorFlyoutClosed;
        this._colorFlyout = null;
    }

    private void TryCompleteClosePreparation()
    {
        if (!this._isPreparingForClose ||
            this._closePreparationCompleted is not { } completed ||
            this._isContextFlyoutOpen ||
            this._isColorFlyoutOpen ||
            !this._isContentPreparedForClose)
        {
            return;
        }

        this._closePreparationCompleted = null;
        if (!this.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => completed()))
        {
            completed();
        }
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        this._isPreparingForClose = true;
        this._pendingContextFlyoutAction = null;
        this._closePreparationCompleted = null;
        this._colorFlyout?.Hide();
        this.DetachColorFlyout();
        this.Closed -= this.OnClosed;
        this._appearance.Dispose();
        this._trayContent.Dispose();
    }
}
