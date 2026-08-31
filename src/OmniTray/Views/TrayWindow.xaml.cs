// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

using System.Numerics;
using System.Runtime.InteropServices;
using Windows.Graphics;
using Windows.UI.ViewManagement;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using OmniTray.Controls;
using WinUIEx.Messaging;
using VirtualKey = Windows.System.VirtualKey;

namespace OmniTray.Views;

public sealed partial class TrayWindow : TransparentWindow
{
    internal event EventHandler? CloseRequested;

    internal event EventHandler? MinimalModeRequested;
    internal const int DefaultWidthInDips = 196;
    internal const int DefaultHeightInDips = 242;
    internal const int EdgeInsetInDips = 20;
    private const int ShadowMarginInDips = 32;
    private const int CompactThumbnailCenterOffsetYInDips = -9;
    private const int InspectorMiddleThumbnailCenterOffsetYInDips = 7;
    private const uint WmNcHitTest = 0x0084;
    private const uint WmExitSizeMove = 0x0232;
    private const int HtCaption = 2;
    private const int HtTransparent = -1;
    private static readonly TimeSpan TransitionDuration = TimeSpan.FromMilliseconds(220);

    private readonly TrayWindowAppearanceController _appearance;
    private readonly DispatcherQueueTimer _autoCollapseTimer;
    private readonly TrayInspector? _inspector;
    private readonly DispatcherQueueTimer _renameDoubleTapTimer;
    private readonly ITrayWindowContent _trayContent;
    private readonly UISettings _uiSettings = new();
    private readonly WindowMessageMonitor? _windowMessageMonitor;
    private Action? _closePreparationCompleted;
    private RectInt32? _compactBounds;
    private Flyout? _colorFlyout;
    private bool _isColorFlyoutOpen;
    private bool _isContentPreparedForClose;
    private bool _isContextFlyoutOpen;
    private bool _isDeleteDialogOpen;
    private bool _isFixedHostConfigured;
    private bool _isInspectorOpen;
    private bool _isPreparingForClose;
    private bool _isRenameDoubleTapPending;
    private bool _isTransitioning;
    private bool _isWindowActive;
    private double _horizontalExpansionOrigin;
    private Action? _pendingContextFlyoutAction;
    private int _shadowMarginInPixels;
    private Storyboard? _transition;
    private double _verticalExpansionOrigin;

    internal TrayContentViewModel ViewModel { get; }

    internal TrayWindow(
        TrayContentViewModel viewModel,
        TrayWindowContentFactory contentFactory)
        : base(false)
    {
        this.ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        ArgumentNullException.ThrowIfNull(contentFactory);
        this.InitializeComponent();
        this._autoCollapseTimer = this.DispatcherQueue.CreateTimer();
        this._autoCollapseTimer.IsRepeating = false;
        this._autoCollapseTimer.Tick += this.OnAutoCollapseTimerTick;
        this._renameDoubleTapTimer = this.DispatcherQueue.CreateTimer();
        this._renameDoubleTapTimer.Interval = TimeSpan.FromMilliseconds(GetDoubleClickTime());
        this._renameDoubleTapTimer.IsRepeating = false;
        this._renameDoubleTapTimer.Tick += this.OnRenameDoubleTapTimerTick;
        this.RootGrid.AddHandler(
            UIElement.DoubleTappedEvent,
            new DoubleTappedEventHandler(this.OnRootDoubleTapped),
            true);
        this._trayContent = contentFactory(this, false);
        this.ContentHost.Content = this._trayContent.View;
        this.AddContentActions();
        if (viewModel is StackTrayContentViewModel stackContent)
        {
            this.CompactTitle.Visibility = Visibility.Collapsed;
            this.CompactDragIndicator.Margin = new Thickness(0);
            this.CompactDragIndicator.VerticalAlignment = VerticalAlignment.Center;
            NoteMenu.SetStack(this.TrayContextFlyout, stackContent.Stack);
            var notes = new NoteIndicator
            {
                Stack = stackContent.Stack,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 8, 8)
            };
            this.CompactSurface.Children.Add(notes);

            this._inspector = new TrayInspector(stackContent.Stack, this);
            this._inspector.UseWindowPresentation();
            this._inspector.CollapseRequested += this.OnInspectorCollapseRequested;
            this._inspector.DeleteRequested += this.OnInspectorDeleteRequested;
            this.InspectorHost.Children.Add(this._inspector);

            var shadowMargin = new Thickness(ShadowMarginInDips);
            this.CompactSurface.Margin = shadowMargin;
            this.InspectorHost.Margin = shadowMargin;
            this.CompactSurface.Shadow = new ThemeShadow();
            this.CompactSurface.Translation = new Vector3(0, 0, 32);
            this._windowMessageMonitor = new WindowMessageMonitor(this);
            this._windowMessageMonitor.WindowMessageReceived += this.OnWindowMessageReceived;
        }

        // TransparentWindow starts hidden, so initialize every x:Bind before the
        // HWND can render its first visible frame.
        this.Bindings.Update();
        this._appearance = new TrayWindowAppearanceController(
            this,
            this.CompactSurface,
            viewModel,
            this.CompactBackdrop,
            this.CompactTintOverlay);

        this.IsAlwaysOnTop = true;
        this.IsMaximizable = false;
        this.IsMinimizable = false;
        this.IsResizable = false;
        this.ApplyTransparentChrome(!this.UsesFixedHost);
        this.SetTitleBar(this.CompactDragRegion);
        this.UpdateChromeOpacity();
        this.AppWindow.Changed += this.OnAppWindowChanged;
        this.Activated += this.OnWindowActivated;
        this.Closed += this.OnClosed;
    }

    internal bool UsesFixedHost => this._inspector is not null;

    internal RectInt32 PersistentBounds
    {
        get
        {
            if (this._isFixedHostConfigured && this._compactBounds is { } compactBounds)
            {
                return compactBounds;
            }

            var position = this.AppWindow.Position;
            var size = this.AppWindow.Size;
            return new RectInt32(position.X, position.Y, size.Width, size.Height);
        }
    }

    internal void EnsureExpandedHost()
    {
        if (!this.UsesFixedHost || this._isFixedHostConfigured)
        {
            return;
        }

        var position = this.AppWindow.Position;
        var size = this.AppWindow.Size;
        this.SetCompactBounds(new RectInt32(position.X, position.Y, size.Width, size.Height));
    }

    internal void SetCompactBounds(RectInt32 compactBounds)
    {
        if (!this.UsesFixedHost)
        {
            this.AppWindow.MoveAndResize(compactBounds);
            return;
        }

        var workArea = DisplayArea.GetFromRect(compactBounds, DisplayAreaFallback.Nearest).WorkArea;
        var inspectorWidth = WindowCoordinator.DipsToPixels(
            this,
            TrayInspector.WindowPresentationWidthInDips);
        var inspectorHeight = WindowCoordinator.DipsToPixels(
            this,
            TrayInspector.WindowPresentationHeightInDips);
        var expansion = TrayWindowPlacement.GetExpansion(
            new System.Drawing.Rectangle(
                compactBounds.X,
                compactBounds.Y,
                compactBounds.Width,
                compactBounds.Height),
            new System.Drawing.Size(inspectorWidth, inspectorHeight),
            new System.Drawing.Point(
                compactBounds.Width / 2,
                (compactBounds.Height / 2) + WindowCoordinator.DipsToPixels(
                    this,
                    CompactThumbnailCenterOffsetYInDips)),
            new System.Drawing.Point(
                inspectorWidth / 2,
                (inspectorHeight / 2) + WindowCoordinator.DipsToPixels(
                    this,
                    InspectorMiddleThumbnailCenterOffsetYInDips)),
            new System.Drawing.Rectangle(workArea.X, workArea.Y, workArea.Width, workArea.Height),
            WindowCoordinator.DipsToPixels(this, EdgeInsetInDips));

        var scale = inspectorWidth / (double)TrayInspector.WindowPresentationWidthInDips;
        var compactWidthInDips = compactBounds.Width / scale;
        var compactHeightInDips = compactBounds.Height / scale;
        this._compactBounds = compactBounds;
        this._horizontalExpansionOrigin = expansion.HorizontalOrigin;
        this._verticalExpansionOrigin = expansion.VerticalOrigin;
        this.CompactSurface.Width = compactWidthInDips;
        this.CompactSurface.Height = compactHeightInDips;
        this.CompactSurface.HorizontalAlignment = HorizontalAlignment.Left;
        this.CompactSurface.VerticalAlignment = VerticalAlignment.Top;
        this.CompactSurface.Margin = new Thickness(
            ShadowMarginInDips +
            (Math.Max(0, TrayInspector.WindowPresentationWidthInDips - compactWidthInDips) *
             expansion.HorizontalOrigin),
            ShadowMarginInDips +
            (Math.Max(0, TrayInspector.WindowPresentationHeightInDips - compactHeightInDips) *
             expansion.VerticalOrigin),
            0,
            0);
        this.InspectorHost.HorizontalAlignment = HorizontalAlignment.Left;
        this.InspectorHost.VerticalAlignment = VerticalAlignment.Top;
        this.InspectorHost.RenderTransformOrigin = new Windows.Foundation.Point(
            expansion.HorizontalOrigin,
            expansion.VerticalOrigin);

        this._shadowMarginInPixels = WindowCoordinator.DipsToPixels(this, ShadowMarginInDips);
        this._isFixedHostConfigured = true;
        this.AppWindow.MoveAndResize(new RectInt32(
            expansion.Bounds.X - this._shadowMarginInPixels,
            expansion.Bounds.Y - this._shadowMarginInPixels,
            expansion.Bounds.Width + (this._shadowMarginInPixels * 2),
            expansion.Bounds.Height + (this._shadowMarginInPixels * 2)));
        this.UpdateWindowRegion();
    }

    private void UpdateWindowRegion()
    {
        if (!this._isFixedHostConfigured ||
            this._compactBounds is not { } compactBounds)
        {
            return;
        }

        if (this._isInspectorOpen)
        {
            this.SetWindowRegion(null);
            return;
        }

        var windowSize = new System.Drawing.Size(this.AppWindow.Size.Width, this.AppWindow.Size.Height);
        var region = TrayWindowPlacement.GetInteractiveBounds(
            windowSize,
            new System.Drawing.Size(compactBounds.Width, compactBounds.Height),
            this._shadowMarginInPixels,
            false,
            this._horizontalExpansionOrigin,
            this._verticalExpansionOrigin);
        region.Inflate(this._shadowMarginInPixels, this._shadowMarginInPixels);
        region.Intersect(new System.Drawing.Rectangle(System.Drawing.Point.Empty, windowSize));
        this.SetWindowRegion(new RectInt32(region.X, region.Y, region.Width, region.Height));
    }

    internal void ShowInspector(TrayInspectorMode mode)
    {
        if (this._inspector is null || this._isPreparingForClose)
        {
            return;
        }

        this._inspector.Open(mode);
        if (!this._isInspectorOpen)
        {
            this.ExpandInspector();
        }
    }

    internal void ShowInspectorFromNameTap()
    {
        if (this._inspector is null || this._isPreparingForClose)
        {
            return;
        }

        this._isRenameDoubleTapPending = true;
        this._renameDoubleTapTimer.Stop();
        this._renameDoubleTapTimer.Start();
        this.ShowInspector(TrayInspectorMode.Browse);
    }

    internal void ShowInspectorFromNameDoubleTap()
    {
        this.CancelPendingRenameDoubleTap();
        this.ShowInspector(TrayInspectorMode.Customize);
    }

    internal async Task ConfirmDeleteAsync()
    {
        if (this._isPreparingForClose ||
            this._isDeleteDialogOpen ||
            this.ViewModel is not StackTrayContentViewModel stackContent)
        {
            return;
        }

        this._isDeleteDialogOpen = true;
        try
        {
            if (await StackDialogService.ConfirmDeleteAsync(this, stackContent.Stack) &&
                App.Current.StackCatalogViewModel.Stacks.Contains(stackContent.Stack))
            {
                await App.Current.DeleteStackAsync(stackContent.Stack);
            }
        }
        finally
        {
            this._isDeleteDialogOpen = false;
        }
    }

    private void ExpandInspector()
    {
        if (this._inspector is null || !this._isFixedHostConfigured)
        {
            return;
        }

        var compactWidth = Math.Max(this.CompactSurface.Width, 1);
        var compactHeight = Math.Max(this.CompactSurface.Height, 1);
        this.CompactSurface.IsHitTestVisible = false;
        this.InspectorScale.ScaleX = Math.Clamp(
            compactWidth / TrayInspector.WindowPresentationWidthInDips,
            0.2,
            1);
        this.InspectorScale.ScaleY = Math.Clamp(
            compactHeight / TrayInspector.WindowPresentationHeightInDips,
            0.2,
            1);
        this.InspectorHost.Opacity = 0;

        this._isInspectorOpen = true;
        this.UpdateWindowRegion();
        this.InspectorHost.Visibility = Visibility.Visible;
        this.SetTitleBar(this._inspector.WindowDragRegion);

        if (!this._uiSettings.AnimationsEnabled)
        {
            this.InspectorScale.ScaleX = 1;
            this.InspectorScale.ScaleY = 1;
            this.InspectorHost.Opacity = 1;
            this.CompleteInspectorExpansion();
            return;
        }

        this.StartTransition(1, 1, 0, this.CompleteInspectorExpansion);
    }

    private void CollapseInspector()
    {
        if (!this._isInspectorOpen || this._isTransitioning || this._compactBounds is null)
        {
            return;
        }

        this._autoCollapseTimer.Stop();

        var compactWidth = Math.Max(this.CompactSurface.Width, 1);
        var compactHeight = Math.Max(this.CompactSurface.Height, 1);
        var scaleX = Math.Clamp(
            compactWidth / TrayInspector.WindowPresentationWidthInDips,
            0.2,
            1);
        var scaleY = Math.Clamp(
            compactHeight / TrayInspector.WindowPresentationHeightInDips,
            0.2,
            1);
        this.CompactSurface.Opacity = 0;
        this.CompactSurface.Visibility = Visibility.Visible;

        if (!this._uiSettings.AnimationsEnabled)
        {
            this.InspectorScale.ScaleX = scaleX;
            this.InspectorScale.ScaleY = scaleY;
            this.InspectorHost.Opacity = 0;
            this.CompleteInspectorCollapse();
            return;
        }

        this.StartTransition(scaleX, scaleY, 1, this.CompleteInspectorCollapse);
    }

    private void StartTransition(
        double inspectorScaleX,
        double inspectorScaleY,
        double compactOpacity,
        Action completed)
    {
        var storyboard = new Storyboard();
        AddTransitionAnimation(storyboard, this.InspectorScale, nameof(ScaleTransform.ScaleX), inspectorScaleX);
        AddTransitionAnimation(storyboard, this.InspectorScale, nameof(ScaleTransform.ScaleY), inspectorScaleY);
        AddTransitionAnimation(storyboard, this.InspectorHost, nameof(UIElement.Opacity), 1 - compactOpacity);
        AddTransitionAnimation(storyboard, this.CompactSurface, nameof(UIElement.Opacity), compactOpacity);
        storyboard.Completed += (_, _) =>
        {
            if (!ReferenceEquals(this._transition, storyboard))
            {
                return;
            }

            this._transition = null;
            this._isTransitioning = false;
            completed();
        };
        this._transition = storyboard;
        this._isTransitioning = true;
        storyboard.Begin();
    }

    private static void AddTransitionAnimation(
        Storyboard storyboard,
        DependencyObject target,
        string property,
        double value)
    {
        var animation = new DoubleAnimation
        {
            To = value,
            Duration = new Duration(TransitionDuration),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        storyboard.Children.Add(animation);
    }

    private void CompleteInspectorExpansion()
    {
        this.CompactSurface.Visibility = Visibility.Collapsed;
        this._inspector?.FocusForWindowPresentation();
        this.UpdateAutoCollapseTimer();
    }

    private void CompleteInspectorCollapse()
    {
        this._autoCollapseTimer.Stop();
        this._isInspectorOpen = false;
        this.InspectorHost.Visibility = Visibility.Collapsed;
        this.InspectorScale.ScaleX = 1;
        this.InspectorScale.ScaleY = 1;
        this.CompactSurface.Opacity = 1;
        this.CompactSurface.IsHitTestVisible = true;
        this.UpdateWindowRegion();
        this.SetTitleBar(this.CompactDragRegion);
        this._inspector?.OnPopupClosed();
    }

    private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        this._isWindowActive = args.WindowActivationState != WindowActivationState.Deactivated;
        this.UpdateChromeOpacity();
        this.UpdateAutoCollapseTimer();
    }

    private void UpdateChromeOpacity()
    {
        var opacity = this._isWindowActive ? 1 : 0.55;
        this.CompactCloseGlyph.Opacity = opacity;
        this.CompactDragIndicator.Opacity = 0.55 * opacity;
        if (this._inspector is { } inspector)
        {
            inspector.WindowDragVisual.Opacity = 0.55 * opacity;
        }
    }

    private void UpdateAutoCollapseTimer()
    {
        this._autoCollapseTimer.Stop();
        if (this._isWindowActive ||
            !this._isInspectorOpen ||
            this._isTransitioning ||
            this._isPreparingForClose)
        {
            return;
        }

        var delay = App.Current.TrayAutoCollapseDelayPreference.GetDuration();
        if (delay == TimeSpan.Zero)
        {
            return;
        }

        this._autoCollapseTimer.Interval = delay;
        this._autoCollapseTimer.Start();
    }

    private void OnAutoCollapseTimerTick(DispatcherQueueTimer sender, object args)
    {
        sender.Stop();
        if (!this._isWindowActive &&
            this._isInspectorOpen &&
            !this._isPreparingForClose &&
            App.Current.TrayAutoCollapseDelayPreference != TrayAutoCollapseDelay.Disabled)
        {
            this.CollapseInspector();
        }
    }

    private void OnRootDoubleTapped(object sender, DoubleTappedRoutedEventArgs args)
    {
        if (!this._isRenameDoubleTapPending)
        {
            return;
        }

        args.Handled = true;
        this.ShowInspectorFromNameDoubleTap();
    }

    private void OnRenameDoubleTapTimerTick(DispatcherQueueTimer sender, object args) =>
        this.CancelPendingRenameDoubleTap();

    private void CancelPendingRenameDoubleTap()
    {
        this._isRenameDoubleTapPending = false;
        this._renameDoubleTapTimer.Stop();
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (!this._isFixedHostConfigured ||
            (!args.DidPositionChange && !args.DidSizeChange) ||
            this._compactBounds is null)
        {
            return;
        }

        this._compactBounds = this.GetCurrentCompactBounds(sender);
    }

    private RectInt32 GetCurrentCompactBounds(AppWindow window)
    {
        this._shadowMarginInPixels = WindowCoordinator.DipsToPixels(this, ShadowMarginInDips);
        var compact = TrayWindowPlacement.GetCompactBounds(
            new System.Drawing.Rectangle(
                window.Position.X + this._shadowMarginInPixels,
                window.Position.Y + this._shadowMarginInPixels,
                Math.Max(0, window.Size.Width - (this._shadowMarginInPixels * 2)),
                Math.Max(0, window.Size.Height - (this._shadowMarginInPixels * 2))),
            new System.Drawing.Size(
                WindowCoordinator.DipsToPixels(this, (int)Math.Round(this.CompactSurface.Width)),
                WindowCoordinator.DipsToPixels(this, (int)Math.Round(this.CompactSurface.Height))),
            this._horizontalExpansionOrigin,
            this._verticalExpansionOrigin);
        return new RectInt32(
            compact.X,
            compact.Y,
            compact.Width,
            compact.Height);
    }

    private void OnWindowMessageReceived(object? sender, WindowMessageEventArgs args)
    {
        if (args.Message.MessageId == WmExitSizeMove)
        {
            // Native dragging moves the fixed host, so rebase its internal expansion origin at rest.
            if (!this._isInspectorOpen &&
                !this._isPreparingForClose &&
                this._isFixedHostConfigured)
            {
                this.SetCompactBounds(this.GetCurrentCompactBounds(this.AppWindow));
            }

            return;
        }

        if (args.Message.MessageId != WmNcHitTest ||
            !this._isFixedHostConfigured ||
            this._compactBounds is not { } compactBounds)
        {
            return;
        }

        var packedPoint = args.Message.LParam.ToInt64();
        var point = new NativePoint
        {
            X = unchecked((short)packedPoint),
            Y = unchecked((short)(packedPoint >> 16))
        };
        if (!ScreenToClient(args.Message.Hwnd, ref point))
        {
            return;
        }

        var interactiveBounds = TrayWindowPlacement.GetInteractiveBounds(
            new System.Drawing.Size(this.AppWindow.Size.Width, this.AppWindow.Size.Height),
            new System.Drawing.Size(compactBounds.Width, compactBounds.Height),
            this._shadowMarginInPixels,
            this._isInspectorOpen,
            this._horizontalExpansionOrigin,
            this._verticalExpansionOrigin);
        if (!interactiveBounds.Contains(point.X, point.Y))
        {
            args.Handled = true;
            args.Result = HtTransparent;
            return;
        }

        if (this._isInspectorOpen && this._inspector is { } inspector)
        {
            var dragHeight = WindowCoordinator.DipsToPixels(
                this,
                (int)Math.Ceiling(inspector.WindowDragRegion.ActualHeight));
            if (TrayWindowPlacement.GetExpandedDragBounds(interactiveBounds, dragHeight)
                .Contains(point.X, point.Y))
            {
                args.Handled = true;
                args.Result = HtCaption;
            }
        }
    }

    private void OnInspectorCollapseRequested(object? sender, EventArgs args) =>
        this.CollapseInspector();

    private void OnInspectorDeleteRequested(object? sender, EventArgs args) =>
        _ = this.ConfirmDeleteAsync();

    private void OnRootKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key == VirtualKey.Escape &&
            this._isInspectorOpen &&
            this._inspector?.TryHandleEscape() == true)
        {
            args.Handled = true;
        }
    }

    private void OnCompactCloseClick(object sender, RoutedEventArgs args) =>
        this.CloseRequested?.Invoke(this, EventArgs.Empty);

    internal void PrepareForClose(Action completed)
    {
        ArgumentNullException.ThrowIfNull(completed);
        if (this._isPreparingForClose)
        {
            return;
        }

        this._isPreparingForClose = true;
        this._autoCollapseTimer.Stop();
        this.CancelPendingRenameDoubleTap();
        this._transition?.Stop();
        this._transition = null;
        this._isTransitioning = false;
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

            var item = new MenuFlyoutItem { Text = action.Text, Icon = new SymbolIcon(action.Icon) };
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
        this._autoCollapseTimer.Stop();
        this.CancelPendingRenameDoubleTap();
        this._transition?.Stop();
        this._transition = null;
        this._pendingContextFlyoutAction = null;
        this._closePreparationCompleted = null;
        this._colorFlyout?.Hide();
        this.DetachColorFlyout();
        this.AppWindow.Changed -= this.OnAppWindowChanged;
        this.Activated -= this.OnWindowActivated;
        this.Closed -= this.OnClosed;
        this._autoCollapseTimer.Tick -= this.OnAutoCollapseTimerTick;
        this._renameDoubleTapTimer.Tick -= this.OnRenameDoubleTapTimerTick;
        if (this._windowMessageMonitor is { } windowMessageMonitor)
        {
            windowMessageMonitor.WindowMessageReceived -= this.OnWindowMessageReceived;
            windowMessageMonitor.Dispose();
        }

        if (this._inspector is { } inspector)
        {
            inspector.CollapseRequested -= this.OnInspectorCollapseRequested;
            inspector.DeleteRequested -= this.OnInspectorDeleteRequested;
            inspector.Dispose();
        }

        this._appearance.Dispose();
        this._trayContent.Dispose();
    }

    [LibraryImport("user32.dll")]
    private static partial uint GetDoubleClickTime();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ScreenToClient(nint windowHandle, ref NativePoint point);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }
}
