// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

using System.ComponentModel;
using System.Runtime.InteropServices;
using Windows.Graphics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using WinRT.Interop;

namespace OmniTray.Services;

internal sealed partial class EdgeWindowController : IDisposable
{
    private const int LeftMouseButton = 0x01;
    private const int HorizontalDragMetric = 68;
    private const int VerticalDragMetric = 69;
    private static readonly TimeSpan HoverRevealDelay = TimeSpan.FromMilliseconds(320);
    private static readonly TimeSpan AutoHideDelay = TimeSpan.FromMilliseconds(700);
    private static readonly EdgeShelfSide[] AllSides = Enum.GetValues<EdgeShelfSide>();
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly DispatcherQueueTimer _displayTimer;
    private readonly DispatcherQueueTimer _gameModeTimer;
    private readonly Dictionary<EdgeHostKey, EdgeWindowHost> _hosts = [];
    private readonly AppSettingsService _settingsService;
    private readonly Func<bool> _isShakeToCreateTrayEnabled;
    private readonly DispatcherQueueTimer _pointerTimer;
    private readonly Action<PointInt32> _shakeToCreateTray;
    private readonly MouseShakeGestureDetector _shakeDetector;

    private readonly MainViewModel _viewModel;
    private NativePoint _buttonDownPoint;
    private DateTimeOffset? _edgeHoverStarted;
    private EdgeHostKey? _hoverHostKey;
    private bool _isClosing;
    private bool _isGameModeSuppressing;
    private IReadOnlyList<string> _knownBusyTriggers = [];
    private WindowActivityHelper.UserNotificationState? _lastNotificationState;
    private bool _leftButtonWasDown;
    private bool _likelyDrag;
    private bool _shakeTriggeredForCurrentPress;

    public EdgeWindowController(
        MainViewModel viewModel,
        DispatcherQueue dispatcherQueue,
        AppSettingsService settingsService,
        Func<bool> isShakeToCreateTrayEnabled,
        Action<PointInt32> shakeToCreateTray)
    {
        this._viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        this._dispatcherQueue = dispatcherQueue ?? throw new ArgumentNullException(nameof(dispatcherQueue));
        this._settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        this._isShakeToCreateTrayEnabled = isShakeToCreateTrayEnabled ??
                                           throw new ArgumentNullException(nameof(isShakeToCreateTrayEnabled));
        this._shakeToCreateTray = shakeToCreateTray ?? throw new ArgumentNullException(nameof(shakeToCreateTray));
        var minimumShakeStroke = Math.Max(
            32,
            Math.Max(
                Math.Abs(GetSystemMetrics(HorizontalDragMetric)),
                Math.Abs(GetSystemMetrics(VerticalDragMetric))) * 4);
        this._shakeDetector = new MouseShakeGestureDetector(minimumShakeStroke);

        this._pointerTimer = dispatcherQueue.CreateTimer();
        this._pointerTimer.Interval = TimeSpan.FromMilliseconds(40);
        this._pointerTimer.IsRepeating = true;
        this._pointerTimer.Tick += this.OnPointerTick;

        this._displayTimer = dispatcherQueue.CreateTimer();
        this._displayTimer.Interval = TimeSpan.FromSeconds(2);
        this._displayTimer.IsRepeating = true;
        this._displayTimer.Tick += this.OnDisplayTick;

        this._gameModeTimer = dispatcherQueue.CreateTimer();
        this._gameModeTimer.Interval = TimeSpan.FromMilliseconds(500);
        this._gameModeTimer.IsRepeating = true;
        this._gameModeTimer.Tick += this.OnGameModeTick;

        this._viewModel.PropertyChanged += this.OnViewModelPropertyChanged;

        this.ReconcileDisplays();
        this.UpdateGameModeState();
        this._pointerTimer.Start();
        this._displayTimer.Start();
        this._gameModeTimer.Start();
    }

    private bool IsRevealSuppressed => this._isGameModeSuppressing || this._viewModel.EdgeWindowsPaused;

    public void Dispose()
    {
        if (this._isClosing)
        {
            return;
        }

        this._isClosing = true;
        this._pointerTimer.Stop();
        this._displayTimer.Stop();
        this._gameModeTimer.Stop();
        this._pointerTimer.Tick -= this.OnPointerTick;
        this._displayTimer.Tick -= this.OnDisplayTick;
        this._gameModeTimer.Tick -= this.OnGameModeTick;
        this._viewModel.PropertyChanged -= this.OnViewModelPropertyChanged;
        this.DisposeHosts();
    }

    public void Show(EdgeShelfSide side = EdgeShelfSide.Right)
    {
        if (this.IsRevealSuppressed || !this._viewModel.IsEdgeWindowEnabled(side))
        {
            return;
        }

        this.ReconcileDisplays();
        var displayArea = TryGetCursorDisplay(out var cursorDisplay, out _)
            ? cursorDisplay
            : DisplayArea.Primary;
        var key = DisplayKey.From(displayArea);
        if (this._hosts.TryGetValue(new EdgeHostKey(key, side), out var host))
        {
            foreach (var candidate in this._hosts.Values.Where(candidate =>
                         candidate.DisplayKey == key && !ReferenceEquals(candidate, host)))
            {
                candidate.Hide(false);
            }

            host.Reveal(true, true);
        }
    }

    public void HideAll(bool animate = true)
    {
        this._hoverHostKey = null;
        this._edgeHoverStarted = null;
        this._likelyDrag = false;
        foreach (var host in this._hosts.Values)
        {
            host.HideForUser(animate);
        }
    }

    private void OnPointerTick(DispatcherQueueTimer sender, object args)
    {
        if (!TryGetCursorDisplay(out var displayArea, out var cursor))
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        this.UpdateLikelyDrag(cursor);
        if (this.TryHandleShake(cursor, now) || this._shakeTriggeredForCurrentPress)
        {
            return;
        }

        if (this.IsRevealSuppressed || !this._viewModel.HasEnabledEdgeWindows)
        {
            return;
        }

        var displayKey = DisplayKey.From(displayArea);
        if (!this._hosts.Keys.Any(key => key.Display == displayKey))
        {
            this.ReconcileDisplays();
        }

        if (this._likelyDrag)
        {
            this.ShowDragHints(displayKey);
        }

        var activationHost = this.FindActivationHost(displayKey, cursor, this._likelyDrag);
        if (activationHost is not null)
        {
            var activationKey = activationHost.Key;
            if (this._hoverHostKey != activationKey)
            {
                this._hoverHostKey = activationKey;
                this._edgeHoverStarted = now;
            }

            if (this._likelyDrag || now - this._edgeHoverStarted >= HoverRevealDelay)
            {
                activationHost.Reveal(false, false);
            }
        }
        else
        {
            this._hoverHostKey = null;
            this._edgeHoverStarted = null;
        }

        foreach (var host in this._hosts.Values)
        {
            if (host.IsPointInsideVisiblePanel(cursor))
            {
                host.NoteInteraction();
            }

            if (this._likelyDrag)
            {
                if (host.DisplayKey != displayKey)
                {
                    host.Hide(false);
                }
                else if (host.IsExpanded &&
                         !host.IsPointInsideVisiblePanel(cursor) &&
                         !host.IsActualDragOver &&
                         now - host.LastInteraction >= AutoHideDelay)
                {
                    host.ShowHint(true);
                }

                continue;
            }

            if (host.IsHintOnly)
            {
                host.Hide(false);
            }
            else if (host.IsExpanded &&
                     !host.IsPointInsideVisiblePanel(cursor) &&
                     !host.IsActualDragOver &&
                     !host.IsWindowActive &&
                     now >= host.ManualOpenUntil &&
                     now - host.LastInteraction >= AutoHideDelay)
            {
                host.Hide(true);
            }
        }
    }

    private void OnDisplayTick(DispatcherQueueTimer sender, object args) => this.ReconcileDisplays();

    private void OnGameModeTick(DispatcherQueueTimer sender, object args) => this.UpdateGameModeState();

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        switch (args.PropertyName)
        {
            case nameof(MainViewModel.GameModeEnabled):
                this.UpdateGameModeState();
                break;

            case nameof(MainViewModel.EdgeWindowsPaused):
                this.ApplyRevealSuppression();
                break;

            case nameof(MainViewModel.LeftEdgeWindowEnabled):
            case nameof(MainViewModel.RightEdgeWindowEnabled):
            case nameof(MainViewModel.TopEdgeWindowEnabled):
            case nameof(MainViewModel.BottomEdgeWindowEnabled):
                this.ReconcileDisplays();
                break;

            case nameof(MainViewModel.LeftEdgeWindowSizeMode):
            case nameof(MainViewModel.LeftEdgeWindowAlignment):
                this.UpdatePlacement(EdgeShelfSide.Left);
                break;

            case nameof(MainViewModel.RightEdgeWindowSizeMode):
            case nameof(MainViewModel.RightEdgeWindowAlignment):
                this.UpdatePlacement(EdgeShelfSide.Right);
                break;

            case nameof(MainViewModel.TopEdgeWindowSizeMode):
            case nameof(MainViewModel.TopEdgeWindowAlignment):
                this.UpdatePlacement(EdgeShelfSide.Top);
                break;

            case nameof(MainViewModel.BottomEdgeWindowSizeMode):
            case nameof(MainViewModel.BottomEdgeWindowAlignment):
                this.UpdatePlacement(EdgeShelfSide.Bottom);
                break;

            case nameof(MainViewModel.HorizontalStackCardDisplayMode):
                this.UpdatePlacement(EdgeShelfSide.Top);
                this.UpdatePlacement(EdgeShelfSide.Bottom);
                break;

            case nameof(MainViewModel.SyncLeftAndRightEdgeContent):
            case nameof(MainViewModel.SyncTopAndBottomEdgeContent):
            case nameof(MainViewModel.SyncAllEdgeContent):
                this.RecreateHosts();
                break;
        }
    }

    private void UpdatePlacement(EdgeShelfSide side)
    {
        foreach (var host in this._hosts.Values.Where(host => host.Key.Side == side))
        {
            host.UpdatePlacement();
        }
    }

    private void UpdateGameModeState()
    {
        if (this._isClosing)
        {
            return;
        }

        var flags = WindowActivityHelper.GetUserNotificationFlags();
        if (flags.State != this._lastNotificationState)
        {
            this._lastNotificationState = flags.State;
            this._knownBusyTriggers = flags.IsBusy
                ? WindowActivityHelper.FindVisibleTriggerApps()
                : [];
        }

        var shouldSuppress = GameModePolicy.ShouldSuppressEdgeWindows(this._viewModel.GameModeEnabled,
            flags.IsRunningD3DFullScreen,
            flags.IsPresentationMode,
            flags.IsBusy);
        var statusText = !this._viewModel.GameModeEnabled
            ? "Game mode is off."
            : shouldSuppress
                ? $"Edge shelves are hidden because Windows reports {WindowActivityHelper.GetStateDisplayName(flags.State)}."
                : "Game mode is on. No fullscreen, presentation, or busy state is active.";
        if (shouldSuppress && this._knownBusyTriggers.Count > 0)
        {
            statusText += $" Visible known trigger: {string.Join(", ", this._knownBusyTriggers)}.";
        }

        this._viewModel.SetGameModeStatus(shouldSuppress, statusText);
        if (this._isGameModeSuppressing == shouldSuppress)
        {
            return;
        }

        this._isGameModeSuppressing = shouldSuppress;
        this.ApplyRevealSuppression();
    }

    private void ApplyRevealSuppression()
    {
        this._hoverHostKey = null;
        this._edgeHoverStarted = null;
        this._likelyDrag = false;
        foreach (var host in this._hosts.Values)
        {
            host.SetRevealSuppressed(this.IsRevealSuppressed);
        }
    }

    private void RecreateHosts()
    {
        if (this._isClosing)
        {
            return;
        }

        this.DisposeHosts();
        this.ReconcileDisplays();
    }

    private void DisposeHosts()
    {
        foreach (var host in this._hosts.Values)
        {
            host.CollapseRequested -= this.OnHostCollapseRequested;
            host.Dispose();
        }

        this._hosts.Clear();
    }

    private void ReconcileDisplays()
    {
        if (this._isClosing)
        {
            return;
        }

        var displayAreas = DisplayArea.FindAll();
        var displaysByKey = new Dictionary<DisplayKey, DisplayArea>();
        for (var index = 0; index < displayAreas.Count; index++)
        {
            var display = displayAreas[index];
            displaysByKey.TryAdd(DisplayKey.From(display), display);
        }

        var displays = displaysByKey.Values.ToArray();
        var desiredKeys = displays
            .SelectMany(display => AllSides
                .Where(this._viewModel.IsEdgeWindowEnabled)
                .Select(side => new EdgeHostKey(DisplayKey.From(display), side)))
            .ToHashSet();

        foreach (var staleKey in this._hosts.Keys.Where(key => !desiredKeys.Contains(key)).ToArray())
        {
            this._hosts.Remove(staleKey, out var staleHost);
            if (staleHost is not null)
            {
                staleHost.CollapseRequested -= this.OnHostCollapseRequested;
                staleHost.Dispose();
            }
        }

        foreach (var display in displays)
        {
            var displayKey = DisplayKey.From(display);
            foreach (var side in AllSides.Where(this._viewModel.IsEdgeWindowEnabled))
            {
                var hostKey = new EdgeHostKey(displayKey, side);
                if (this._hosts.TryGetValue(hostKey, out var existingHost))
                {
                    existingHost.UpdateDisplayArea(display.OuterBounds, display.WorkArea);
                    continue;
                }

                var host = new EdgeWindowHost(
                    hostKey,
                    display.OuterBounds,
                    display.WorkArea,
                    this._viewModel,
                    this._settingsService,
                    () => this.IsRevealSuppressed);
                host.CollapseRequested += this.OnHostCollapseRequested;
                this._hosts.Add(hostKey, host);
            }
        }
    }

    private void ShowDragHints(DisplayKey currentDisplay)
    {
        foreach (var host in this._hosts.Values)
        {
            if (host.DisplayKey == currentDisplay)
            {
                if (!host.IsExpanded)
                {
                    host.ShowHint(false);
                }
            }
            else
            {
                host.Hide(false);
            }
        }
    }

    private EdgeWindowHost? FindActivationHost(DisplayKey display, NativePoint cursor, bool useHintBand)
    {
        foreach (var side in AllSides)
        {
            if (this._hosts.TryGetValue(new EdgeHostKey(display, side), out var host) &&
                host.IsPointInActivationZone(cursor, useHintBand))
            {
                return host;
            }
        }

        return null;
    }

    private void UpdateLikelyDrag(NativePoint cursor)
    {
        var leftButtonDown = GetAsyncKeyState(LeftMouseButton) < 0;
        if (leftButtonDown && !this._leftButtonWasDown)
        {
            this._buttonDownPoint = cursor;
            this._likelyDrag = false;
            this._shakeTriggeredForCurrentPress = false;
            this._shakeDetector.Reset();
        }
        else if (leftButtonDown && !this._likelyDrag)
        {
            var horizontalThreshold = Math.Max(1, Math.Abs(GetSystemMetrics(HorizontalDragMetric)));
            var verticalThreshold = Math.Max(1, Math.Abs(GetSystemMetrics(VerticalDragMetric)));
            this._likelyDrag = Math.Abs(cursor.X - this._buttonDownPoint.X) > horizontalThreshold ||
                               Math.Abs(cursor.Y - this._buttonDownPoint.Y) > verticalThreshold;
        }
        else if (!leftButtonDown)
        {
            this._likelyDrag = false;
            this._shakeTriggeredForCurrentPress = false;
            this._shakeDetector.Reset();
        }

        this._leftButtonWasDown = leftButtonDown;
    }

    private bool TryHandleShake(NativePoint cursor, DateTimeOffset now)
    {
        if (!this._isShakeToCreateTrayEnabled() || !this._likelyDrag || this._shakeTriggeredForCurrentPress)
        {
            return false;
        }

        if (!this._shakeDetector.Update(cursor.X, cursor.Y, now))
        {
            return false;
        }

        this._shakeTriggeredForCurrentPress = true;
        foreach (var host in this._hosts.Values)
        {
            host.Hide(false);
        }

        this._shakeToCreateTray(new PointInt32(cursor.X, cursor.Y));
        return true;
    }

    private void OnHostCollapseRequested(object? sender, EventArgs args)
    {
        if (sender is EdgeWindowHost host)
        {
            host.Hide(true);
        }
    }

    private static bool TryGetCursorDisplay(out DisplayArea displayArea, out NativePoint cursor)
    {
        if (!GetCursorPos(out cursor))
        {
            displayArea = DisplayArea.Primary;
            return false;
        }

        displayArea = DisplayArea.GetFromPoint(
            new PointInt32(cursor.X, cursor.Y),
            DisplayAreaFallback.Nearest);
        return true;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out NativePoint point);

    [LibraryImport("user32.dll")]
    private static partial short GetAsyncKeyState(int virtualKey);

    [LibraryImport("user32.dll")]
    private static partial int GetSystemMetrics(int index);

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativePoint
    {
        public int X;

        public int Y;
    }

    private readonly record struct DisplayKey(ulong Value)
    {
        public static DisplayKey From(DisplayArea display) => new(display.DisplayId.Value);
    }

    private readonly record struct EdgeHostKey(DisplayKey Display, EdgeShelfSide Side);

    private sealed partial class EdgeWindowHost : IDisposable
    {
        private const int ExtendedWindowStyle = -20;
        private const nint TransparentExtendedStyle = 0x00000020;
        private const nint NoActivateExtendedStyle = 0x08000000;
        private const int DesignVerticalWidth = OmniTrayPopupWindow.DefaultWidthInDips;
        private const int DesignVerticalHeight = 680;
        private const int DesignHorizontalWidth = 760;
        private const int DesignShadowMargin = 48;
        private const int DesignExpandedInset = 11;
        private const int MinimumVerticalDockThickness = 240;
        private const int MinimumRemainingWorkspace = 160;
        private const int SmallVerticalDockThickness = 320;
        private const int LargeVerticalDockThickness = 560;
        private static readonly TimeSpan ManualOpenGracePeriod = TimeSpan.FromSeconds(2);
        private readonly EdgeAppBarRegistration _appBar;
        private readonly Func<bool> _isRevealSuppressed;
        private readonly AppSettingsService _settingsService;
        private readonly MainViewModel _viewModel;

        private readonly EdgeWindow _window;
        private readonly nint _windowHandle;
        private RectInt32 _outerBounds;
        private RectInt32 _workArea;
        private NativePoint? _dockResizeStartCursor;
        private double _dockResizeStartThicknessInDips;
        private double? _dockedThicknessInDips;
        private int _expandedInset;
        private int _hintThickness;
        private int _hostHeight;
        private int _hostWidth;
        private int _hostX;
        private int _hostY;
        private bool _isDocked;
        private bool _isDockTemporarilyHidden;
        private bool _isClickThrough;
        private bool _isClosing;
        private bool _isDockResizeActive;
        private bool _isFullScreenAppOpen;
        private bool _isUpdatingAppBarPosition;
        private bool _isTargetExpanded;
        private int _panelHeight;
        private int _panelWidth;
        private int _panelX;
        private int _panelY;
        private double _scale;
        private int _shadowMargin;
        private RectInt32 _visiblePanelRect;

        public EdgeWindowHost(
            EdgeHostKey key,
            RectInt32 outerBounds,
            RectInt32 workArea,
            MainViewModel viewModel,
            AppSettingsService settingsService,
            Func<bool> isRevealSuppressed)
        {
            this.Key = key;
            this._outerBounds = outerBounds;
            this._workArea = workArea;
            this._viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            this._settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            this._isRevealSuppressed
                = isRevealSuppressed ?? throw new ArgumentNullException(nameof(isRevealSuppressed));
            this._window = new EdgeWindow(this._viewModel, key.Side);
            this._windowHandle = WindowNative.GetWindowHandle(this._window);
            this._appBar = new EdgeAppBarRegistration(
                this._windowHandle,
                this.OnAppBarPositionChanged,
                this.OnShellRestarted,
                this.OnFullScreenAppChanged);
            this._isDocked = this._settingsService.GetEdgeWindowDocked(key.Display.Value, key.Side);
            this._dockedThicknessInDips = this._settingsService.GetEdgeWindowDockThickness(
                key.Display.Value,
                key.Side);
            this._window.SetDockedState(this._isDocked);
            this.ConfigureWindow();
            if (!this.TryApplyDockedState())
            {
                this.ConfigurePlacement(true);
            }

            this._window.CollapseRequested += this.OnCollapseRequested;
            this._window.DockToggled += this.OnDockToggled;
            this._window.DockResizeStarted += this.OnDockResizeStarted;
            this._window.DockResizeDelta += this.OnDockResizeDelta;
            this._window.DockResizeCompleted += this.OnDockResizeCompleted;
            this._window.DockResizePresetRequested += this.OnDockResizePresetRequested;
            this._window.PointerInteractionStarted += this.OnPointerInteractionStarted;
            this._window.PointerInteractionEnded += this.OnPointerInteractionEnded;
            this._window.ExternalDragEntered += this.OnExternalDragEntered;
            this._window.ExternalDragLeft += this.OnExternalDragLeft;
            this._window.DropCompleted += this.OnDropCompleted;
            this._window.HorizontalDetailExpansionChanged += this.OnHorizontalDetailExpansionChanged;
            this._window.Activated += this.OnWindowActivated;
            this._window.AppWindow.Closing += this.OnWindowClosing;

            this.LastInteraction = DateTimeOffset.UtcNow;
            if (!this.IsDocked)
            {
                this._visiblePanelRect = this.GetPanelRect(false);
            }
        }

        public EdgeHostKey Key { get; }

        public DisplayKey DisplayKey => this.Key.Display;

        public bool IsExpanded { get; private set; }

        public bool IsDocked => this._isDocked;

        public bool IsHintOnly => this._window.AppWindow.IsVisible && !this.IsExpanded && !this._isTargetExpanded;

        public bool IsActualDragOver { get; private set; }

        public bool IsWindowActive { get; private set; }

        public DateTimeOffset LastInteraction { get; private set; }

        public DateTimeOffset ManualOpenUntil { get; private set; }

        public void Dispose()
        {
            if (this._isClosing)
            {
                return;
            }

            this._isClosing = true;
            this._window.CollapseRequested -= this.OnCollapseRequested;
            this._window.DockToggled -= this.OnDockToggled;
            this._window.DockResizeStarted -= this.OnDockResizeStarted;
            this._window.DockResizeDelta -= this.OnDockResizeDelta;
            this._window.DockResizeCompleted -= this.OnDockResizeCompleted;
            this._window.DockResizePresetRequested -= this.OnDockResizePresetRequested;
            this._window.PointerInteractionStarted -= this.OnPointerInteractionStarted;
            this._window.PointerInteractionEnded -= this.OnPointerInteractionEnded;
            this._window.ExternalDragEntered -= this.OnExternalDragEntered;
            this._window.ExternalDragLeft -= this.OnExternalDragLeft;
            this._window.DropCompleted -= this.OnDropCompleted;
            this._window.HorizontalDetailExpansionChanged -= this.OnHorizontalDetailExpansionChanged;
            this._window.Activated -= this.OnWindowActivated;
            this._window.AppWindow.Closing -= this.OnWindowClosing;
            this._appBar.Dispose();
            this._window.Detach();
            this._window.Close();
        }

        public event EventHandler? CollapseRequested;

        public void ShowHint(bool animate)
        {
            if (this.IsDocked)
            {
                this._isDockTemporarilyHidden = false;
                this.TryApplyDockedState();
                return;
            }

            if (this._isRevealSuppressed())
            {
                this.Hide(false);
                return;
            }

            var wasTargetExpanded = this._isTargetExpanded;
            this.IsExpanded = false;
            this._isTargetExpanded = false;
            this.ManualOpenUntil = DateTimeOffset.MinValue;
            this.SetClickThrough(true);
            this._visiblePanelRect = this.GetPanelRect(false);
            if (!this._window.AppWindow.IsVisible)
            {
                this._window.SetRevealState(false, false);
                this._window.AppWindow.Show(false);
                return;
            }

            this._window.SetRevealState(
                false,
                animate && wasTargetExpanded);
        }

        public void Reveal(bool activateWindow, bool manualOpen)
        {
            if (this.IsDocked)
            {
                this._isDockTemporarilyHidden = false;
                this.TryApplyDockedState();
                return;
            }

            if (this._isRevealSuppressed())
            {
                this.Hide(false);
                return;
            }

            var wasVisible = this._window.AppWindow.IsVisible;
            var needsAnimation = !this._isTargetExpanded || !wasVisible;
            this.IsExpanded = true;
            this._isTargetExpanded = true;
            this._visiblePanelRect = this.GetPanelRect(true);
            this.LastInteraction = DateTimeOffset.UtcNow;
            if (manualOpen)
            {
                this.ManualOpenUntil = this.LastInteraction + ManualOpenGracePeriod;
            }

            this.SetClickThrough(false);
            if (!wasVisible)
            {
                this._window.SetRevealState(false, false);
                this._window.AppWindow.Show(activateWindow);
            }
            else if (activateWindow)
            {
                this._window.AppWindow.Show(true);
            }

            if (needsAnimation)
            {
                this._window.SetRevealState(true, true);
            }
        }

        public void Hide(bool animate)
        {
            if (this.IsDocked && !this._isRevealSuppressed())
            {
                return;
            }

            if (!this._window.AppWindow.IsVisible)
            {
                return;
            }

            var wasTargetExpanded = this._isTargetExpanded;
            this.IsExpanded = false;
            this._isTargetExpanded = false;
            this.IsActualDragOver = false;
            this.ManualOpenUntil = DateTimeOffset.MinValue;
            this.SetClickThrough(true);
            this._visiblePanelRect = this.GetPanelRect(false);
            if (animate && wasTargetExpanded)
            {
                this._window.SetRevealState(
                    false,
                    true,
                    () =>
                    {
                        if (!this._isClosing && !this._isTargetExpanded)
                        {
                            this.HideWindow();
                        }
                    });
            }
            else
            {
                this._window.SetRevealState(false, false);
                this.HideWindow();
            }
        }

        public void HideForUser(bool animate)
        {
            if (!this.IsDocked)
            {
                this.Hide(animate);
                return;
            }

            this._isDockTemporarilyHidden = true;
            this._appBar.Unregister();
            this.RefreshDisplayArea();
            this.ConfigurePlacement(true);
            this.IsExpanded = false;
            this._isTargetExpanded = false;
            this.IsActualDragOver = false;
            this.ManualOpenUntil = DateTimeOffset.MinValue;
            this.SetClickThrough(true);
            this._visiblePanelRect = this.GetPanelRect(false);
            this._window.AppWindow.Hide();
        }

        private void HideWindow()
        {
            this._window.ResetCommandNavigation();
            this._window.AppWindow.Hide();
        }

        public bool IsPointInActivationZone(NativePoint point, bool useHintBand)
        {
            if (this.IsDocked && !this._isDockTemporarilyHidden)
            {
                return false;
            }

            var band = useHintBand ? this._hintThickness : Math.Max(3, (int)Math.Round(4 * this._scale));
            var withinHorizontalSpan = point.X >= this._hostX && point.X < this._hostX + this._hostWidth;
            var withinVerticalSpan = point.Y >= this._hostY && point.Y < this._hostY + this._hostHeight;
            return this.Key.Side switch
            {
                EdgeShelfSide.Left => withinVerticalSpan &&
                                      point.X >= this._workArea.X && point.X < this._workArea.X + band,
                EdgeShelfSide.Right => withinVerticalSpan &&
                                       point.X >= this._workArea.X + this._workArea.Width - band &&
                                       point.X < this._workArea.X + this._workArea.Width,
                EdgeShelfSide.Top => withinHorizontalSpan &&
                                     point.Y >= this._workArea.Y && point.Y < this._workArea.Y + band,
                EdgeShelfSide.Bottom => withinHorizontalSpan &&
                                        point.Y >= this._workArea.Y + this._workArea.Height - band &&
                                        point.Y < this._workArea.Y + this._workArea.Height,
                _ => false
            };
        }

        public bool IsPointInsideVisiblePanel(NativePoint point) =>
            this._window.AppWindow.IsVisible &&
            point.X >= this._hostX + this._visiblePanelRect.X &&
            point.X < this._hostX + this._visiblePanelRect.X + this._visiblePanelRect.Width &&
            point.Y >= this._hostY + this._visiblePanelRect.Y &&
            point.Y < this._hostY + this._visiblePanelRect.Y + this._visiblePanelRect.Height;

        public void NoteInteraction() => this.LastInteraction = DateTimeOffset.UtcNow;

        public void UpdatePlacement()
        {
            if (this.TryApplyDockedState())
            {
                return;
            }

            this.ConfigurePlacement();
            this._visiblePanelRect = this.GetPanelRect(this._isTargetExpanded);
        }

        public void UpdateDisplayArea(RectInt32 outerBounds, RectInt32 workArea)
        {
            if (this._outerBounds.Equals(outerBounds) && this._workArea.Equals(workArea))
            {
                return;
            }

            this._outerBounds = outerBounds;
            this._workArea = workArea;
            this.UpdatePlacement();
        }

        public void SetRevealSuppressed(bool suppressed)
        {
            if (!this.IsDocked)
            {
                if (suppressed)
                {
                    this.Hide(false);
                }

                return;
            }

            if (suppressed)
            {
                this._appBar.Unregister();
                this._window.AppWindow.Hide();
                return;
            }

            this.TryApplyDockedState();
        }

        private bool TryApplyDockedState()
        {
            if (!this.IsDocked)
            {
                return false;
            }

            if (this._isRevealSuppressed())
            {
                this._appBar.Unregister();
                this._window.AppWindow.Hide();
                return true;
            }

            if (this._isDockTemporarilyHidden)
            {
                return true;
            }

            if (!this._appBar.Register())
            {
                this._isDocked = false;
                this._window.SetDockedState(false);
                this._settingsService.SetEdgeWindowDocked(this.Key.Display.Value, this.Key.Side, false);
                this._window.ShowDockingError();
                return false;
            }

            this.ConfigureDockedPlacement();
            return true;
        }

        private void ConfigureDockedPlacement()
        {
            if (this._isClosing || !this._appBar.IsRegistered || this._isUpdatingAppBarPosition)
            {
                return;
            }

            this._isUpdatingAppBarPosition = true;
            try
            {
                // Moving first makes GetDpiForWindow observe the target display before
                // the shell negotiates the final AppBar rectangle.
                var currentPosition = this._window.AppWindow.Position;
                if (currentPosition.X < this._outerBounds.X ||
                    currentPosition.X >= this._outerBounds.X + this._outerBounds.Width ||
                    currentPosition.Y < this._outerBounds.Y ||
                    currentPosition.Y >= this._outerBounds.Y + this._outerBounds.Height)
                {
                    this._window.AppWindow.Move(new PointInt32(this._outerBounds.X, this._outerBounds.Y));
                }
                var dpi = GetDpiForWindow(this._windowHandle);
                this._scale = dpi == 0 ? 1 : dpi / 96d;
                this._hintThickness = Math.Max(1, (int)Math.Round(EdgeWindow.HintThickness * this._scale));
                var designThickness = this.ClampDockedThickness(
                    this._dockedThicknessInDips ?? this.GetDefaultDockedThickness());
                var maximumThickness = this.Key.Side.IsVertical()
                    ? this._outerBounds.Width
                    : this._outerBounds.Height;
                var thickness = Math.Clamp(
                    (int)Math.Round(designThickness * this._scale),
                    this._hintThickness,
                    Math.Max(this._hintThickness, maximumThickness));
                var rectangle = this._appBar.UpdatePosition(this.Key.Side, this._outerBounds, thickness);
                if (rectangle.Width <= 0 || rectangle.Height <= 0)
                {
                    return;
                }

                this._expandedInset = 0;
                this._shadowMargin = 0;
                this._hostX = rectangle.X;
                this._hostY = rectangle.Y;
                this._hostWidth = rectangle.Width;
                this._hostHeight = rectangle.Height;
                this._panelX = 0;
                this._panelY = 0;
                this._panelWidth = rectangle.Width;
                this._panelHeight = rectangle.Height;
                this._visiblePanelRect = new RectInt32(0, 0, rectangle.Width, rectangle.Height);
                this.IsExpanded = true;
                this._isTargetExpanded = true;
                this.IsActualDragOver = false;
                this.ManualOpenUntil = DateTimeOffset.MaxValue;
                this.SetClickThrough(false);
                this._window.ConfigurePanelSize(
                    rectangle.Width / this._scale,
                    rectangle.Height / this._scale,
                    0,
                    0,
                    0);
                this._window.SetRevealState(true, false);
                if (!this._window.AppWindow.IsVisible)
                {
                    this._window.AppWindow.Show(false);
                }

                this.UpdateDockTopmostState();
            }
            finally
            {
                this._isUpdatingAppBarPosition = false;
            }
        }

        private void SetDocked(bool docked)
        {
            if (this._isDocked == docked)
            {
                return;
            }

            this._isDocked = docked;
            this._dockResizeStartCursor = null;
            this._isDockResizeActive = false;
            this._isDockTemporarilyHidden = false;
            this._window.SetDockedState(docked);
            if (docked)
            {
                if (!this.TryApplyDockedState())
                {
                    this.ConfigurePlacement(true);
                }
            }
            else
            {
                this._appBar.Unregister();
                this.RefreshDisplayArea();
                this._isFullScreenAppOpen = false;
                this.UpdateDockTopmostState();
                this.ConfigurePlacement(true);
                this.Hide(false);
            }

            this._settingsService.SetEdgeWindowDocked(
                this.Key.Display.Value,
                this.Key.Side,
                this._isDocked);
        }

        private void RefreshDisplayArea()
        {
            var displayAreas = DisplayArea.FindAll();
            DisplayArea? display = null;
            for (var index = 0; index < displayAreas.Count; index++)
            {
                var candidate = displayAreas[index];
                if (candidate.DisplayId.Value == this.Key.Display.Value)
                {
                    display = candidate;
                    break;
                }
            }

            if (display is null)
            {
                return;
            }

            this._outerBounds = display.OuterBounds;
            this._workArea = display.WorkArea;
        }

        private void OnDockToggled(object? sender, EventArgs args) => this.SetDocked(!this.IsDocked);

        private void OnDockResizeStarted(object? sender, EventArgs args)
        {
            this._dockResizeStartCursor = null;
            this._isDockResizeActive = this.IsDocked && !this._isDockTemporarilyHidden;
            if (!this._isDockResizeActive || !GetCursorPos(out var cursor))
            {
                return;
            }

            var scale = this._scale > 0 ? this._scale : 1;
            this._dockResizeStartCursor = cursor;
            this._dockResizeStartThicknessInDips = this.Key.Side.IsVertical()
                ? this._hostWidth / scale
                : this._hostHeight / scale;
        }

        private void OnDockResizeDelta(object? sender, DockResizeDeltaEventArgs args)
        {
            if (!this.IsDocked || this._isDockTemporarilyHidden || !double.IsFinite(args.Change))
            {
                return;
            }

            var scale = this._scale > 0 ? this._scale : 1;
            double requestedThickness;
            if (this._dockResizeStartCursor is { } startCursor && GetCursorPos(out var currentCursor))
            {
                var screenChange = this.Key.Side.IsVertical()
                    ? currentCursor.X - startCursor.X
                    : currentCursor.Y - startCursor.Y;
                var inwardChange = this.Key.Side is EdgeShelfSide.Left or EdgeShelfSide.Top
                    ? screenChange / scale
                    : -screenChange / scale;
                requestedThickness = this._dockResizeStartThicknessInDips + inwardChange;
            }
            else
            {
                var currentThickness = this.Key.Side.IsVertical()
                    ? this._hostWidth / scale
                    : this._hostHeight / scale;
                var inwardChange = this.Key.Side is EdgeShelfSide.Left or EdgeShelfSide.Top
                    ? args.Change
                    : -args.Change;
                requestedThickness = currentThickness + inwardChange;
            }

            this._dockedThicknessInDips = this.ClampDockedThickness(requestedThickness);
            this.ConfigureDockedPlacement();
            this.NoteInteraction();
        }

        private void OnDockResizeCompleted(object? sender, EventArgs args)
        {
            this._dockResizeStartCursor = null;
            this._isDockResizeActive = false;
            this.ConfigureDockedPlacement();
            this.PersistDockedThickness();
            this.NoteInteraction();
        }

        private void OnDockResizePresetRequested(object? sender, DockResizePresetEventArgs args)
        {
            if (!this.IsDocked || this._isDockTemporarilyHidden)
            {
                return;
            }

            this._dockResizeStartCursor = null;
            this._isDockResizeActive = false;
            this._dockedThicknessInDips = this.ClampDockedThickness(this.GetDockedThicknessPreset(args.Preset));
            this.ConfigureDockedPlacement();
            this.PersistDockedThickness();
            this.NoteInteraction();
        }

        private void PersistDockedThickness()
        {
            if (this._dockedThicknessInDips is not { } thickness)
            {
                return;
            }

            this._settingsService.SetEdgeWindowDockThickness(
                this.Key.Display.Value,
                this.Key.Side,
                thickness);
        }

        private double GetDefaultDockedThickness() =>
            this.Key.Side.IsVertical()
                ? DesignVerticalWidth
                : this._window.IsHorizontalDetailExpanded
                    ? this._viewModel.HorizontalStackCardLayout.HorizontalPanelExpandedHeight
                    : this._viewModel.HorizontalStackCardLayout.HorizontalPanelCollapsedHeight;

        private double GetDockedThicknessPreset(DockSizePreset preset)
        {
            if (this.Key.Side.IsVertical())
            {
                return preset switch
                {
                    DockSizePreset.Small => SmallVerticalDockThickness,
                    DockSizePreset.Medium => DesignVerticalWidth,
                    DockSizePreset.Large => LargeVerticalDockThickness,
                    _ => throw new ArgumentOutOfRangeException(nameof(preset))
                };
            }

            var collapsedHeight = this._viewModel.HorizontalStackCardLayout.HorizontalPanelCollapsedHeight;
            var expandedHeight = this._viewModel.HorizontalStackCardLayout.HorizontalPanelExpandedHeight;
            return preset switch
            {
                DockSizePreset.Small => collapsedHeight,
                DockSizePreset.Medium => Math.Round((collapsedHeight + expandedHeight) / 2),
                DockSizePreset.Large => expandedHeight,
                _ => throw new ArgumentOutOfRangeException(nameof(preset))
            };
        }

        private double ClampDockedThickness(double thickness)
        {
            var minimumThickness = this.Key.Side.IsVertical()
                ? MinimumVerticalDockThickness
                : this._viewModel.HorizontalStackCardLayout.HorizontalPanelCollapsedHeight;
            var scale = this._scale > 0 ? this._scale : 1;
            var displayThickness = (this.Key.Side.IsVertical()
                ? this._outerBounds.Width
                : this._outerBounds.Height) / scale;
            var maximumThickness = Math.Max(minimumThickness, displayThickness - MinimumRemainingWorkspace);
            return Math.Clamp(thickness, minimumThickness, maximumThickness);
        }

        private void OnAppBarPositionChanged()
        {
            if (this.IsDocked &&
                !this._isDockTemporarilyHidden &&
                !this._isDockResizeActive &&
                !this._isRevealSuppressed())
            {
                this.ConfigureDockedPlacement();
            }
        }

        private void OnShellRestarted()
        {
            if (this.IsDocked && !this._isDockTemporarilyHidden && !this._isRevealSuppressed())
            {
                this.TryApplyDockedState();
            }
        }

        private void OnFullScreenAppChanged(bool fullScreenAppOpen)
        {
            this._isFullScreenAppOpen = fullScreenAppOpen;
            this.UpdateDockTopmostState();
        }

        private void UpdateDockTopmostState()
        {
            if (this._window.AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsAlwaysOnTop = !this.IsDocked || !this._isFullScreenAppOpen;
            }

            if (this.IsDocked)
            {
                this._appBar.SetFullScreenState(this._isFullScreenAppOpen);
            }
        }

        private void ConfigureWindow()
        {
            if (this._window.AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsAlwaysOnTop = true;
                presenter.IsMaximizable = false;
                presenter.IsMinimizable = false;
                presenter.IsResizable = false;
            }
        }

        private void ConfigurePlacement(bool moveToWorkAreaForDpi = false)
        {
            if (moveToWorkAreaForDpi)
            {
                this._window.AppWindow.Move(new PointInt32(this._workArea.X, this._workArea.Y));
            }

            var dpi = GetDpiForWindow(this._windowHandle);
            this._scale = dpi == 0 ? 1 : dpi / 96d;
            this._hintThickness = Math.Max(1, (int)Math.Round(EdgeWindow.HintThickness * this._scale));
            this._expandedInset = Math.Max(0, (int)Math.Round(DesignExpandedInset * this._scale));
            this._shadowMargin = Math.Max(0, (int)Math.Round(DesignShadowMargin * this._scale));
            var sizeMode = this._viewModel.GetEdgeWindowSizeMode(this.Key.Side);
            var alignment = this._viewModel.GetEdgeWindowAlignment(this.Key.Side);

            if (this.Key.Side.IsVertical())
            {
                this._panelWidth = Math.Min(
                    Math.Max(this._hintThickness, this._workArea.Width - this._expandedInset - this._shadowMargin),
                    Math.Max(this._hintThickness, (int)Math.Round(DesignVerticalWidth * this._scale)));
                var leadingMargin = sizeMode == EdgeWindowSizeMode.Stretch || alignment == EdgeWindowAlignment.Start
                    ? this._expandedInset
                    : this._shadowMargin;
                var trailingMargin = sizeMode == EdgeWindowSizeMode.Stretch || alignment == EdgeWindowAlignment.End
                    ? this._expandedInset
                    : this._shadowMargin;
                var availableHeight = Math.Max(
                    this._hintThickness,
                    this._workArea.Height - leadingMargin - trailingMargin);
                this._panelHeight = sizeMode == EdgeWindowSizeMode.Stretch
                    ? availableHeight
                    : Math.Min(
                        availableHeight,
                        Math.Max(this._hintThickness, (int)Math.Round(DesignVerticalHeight * this._scale)));
                this._hostWidth = this._panelWidth + this._expandedInset + this._shadowMargin;
                this._hostHeight = this._panelHeight + leadingMargin + trailingMargin;
                this._hostX = this.Key.Side == EdgeShelfSide.Left
                    ? this._workArea.X
                    : this._workArea.X + this._workArea.Width - this._hostWidth;
                this._hostY = this._workArea.Y + ResolveAxisOffset(
                    this._workArea.Height,
                    this._hostHeight,
                    sizeMode,
                    alignment);
                this._panelX = this.Key.Side == EdgeShelfSide.Left ? this._expandedInset : this._shadowMargin;
                this._panelY = leadingMargin;
            }
            else
            {
                var designHeight = this._window.IsHorizontalDetailExpanded
                    ? this._viewModel.HorizontalStackCardLayout.HorizontalPanelExpandedHeight
                    : this._viewModel.HorizontalStackCardLayout.HorizontalPanelCollapsedHeight;
                var leadingMargin = sizeMode == EdgeWindowSizeMode.Stretch || alignment == EdgeWindowAlignment.Start
                    ? this._expandedInset
                    : this._shadowMargin;
                var trailingMargin = sizeMode == EdgeWindowSizeMode.Stretch || alignment == EdgeWindowAlignment.End
                    ? this._expandedInset
                    : this._shadowMargin;
                var availableWidth = Math.Max(
                    this._hintThickness,
                    this._workArea.Width - leadingMargin - trailingMargin);
                this._panelWidth = sizeMode == EdgeWindowSizeMode.Stretch
                    ? availableWidth
                    : Math.Min(
                        availableWidth,
                        Math.Max(this._hintThickness, (int)Math.Round(DesignHorizontalWidth * this._scale)));
                this._panelHeight = Math.Min(
                    Math.Max(this._hintThickness, this._workArea.Height - this._expandedInset - this._shadowMargin),
                    Math.Max(this._hintThickness, (int)Math.Round(designHeight * this._scale)));
                this._hostWidth = this._panelWidth + leadingMargin + trailingMargin;
                this._hostHeight = this._panelHeight + this._expandedInset + this._shadowMargin;
                this._hostX = this._workArea.X + ResolveAxisOffset(
                    this._workArea.Width,
                    this._hostWidth,
                    sizeMode,
                    alignment);
                this._hostY = this.Key.Side == EdgeShelfSide.Top
                    ? this._workArea.Y
                    : this._workArea.Y + this._workArea.Height - this._hostHeight;
                this._panelX = leadingMargin;
                this._panelY = this.Key.Side == EdgeShelfSide.Top ? this._expandedInset : this._shadowMargin;
            }

            // The shadow host grows inward, but the expanded panel itself stays exactly
            // DesignExpandedInset DIPs from its monitor edge on every side.

            this._window.ConfigurePanelSize(this._panelWidth / this._scale, this._panelHeight / this._scale,
                this._panelX / this._scale, this._panelY / this._scale, this._expandedInset / this._scale);
            this._window.AppWindow.MoveAndResize(new RectInt32(this._hostX, this._hostY, this._hostWidth,
                this._hostHeight));
        }

        private static int ResolveAxisOffset(
            int workAreaLength,
            int hostLength,
            EdgeWindowSizeMode sizeMode,
            EdgeWindowAlignment alignment)
        {
            if (sizeMode == EdgeWindowSizeMode.Stretch)
            {
                return 0;
            }

            var remaining = Math.Max(0, workAreaLength - hostLength);
            return alignment switch
            {
                EdgeWindowAlignment.Start => 0,
                EdgeWindowAlignment.Center => remaining / 2,
                EdgeWindowAlignment.End => remaining,
                _ => throw new ArgumentOutOfRangeException(nameof(alignment))
            };
        }

        private void OnHorizontalDetailExpansionChanged(object? sender, EventArgs args)
        {
            if (this._isClosing || this.Key.Side.IsVertical())
            {
                return;
            }

            this.UpdatePlacement();
            this.NoteInteraction();
        }

        private RectInt32 GetPanelRect(bool expanded)
        {
            if (expanded)
            {
                return new RectInt32(this._panelX, this._panelY, this._panelWidth, this._panelHeight);
            }

            return this.Key.Side switch
            {
                EdgeShelfSide.Left => new RectInt32(0, this._panelY, this._hintThickness, this._panelHeight),
                EdgeShelfSide.Right => new RectInt32(this._hostWidth - this._hintThickness, this._panelY,
                    this._hintThickness, this._panelHeight),
                EdgeShelfSide.Top => new RectInt32(this._panelX, 0, this._panelWidth, this._hintThickness),
                EdgeShelfSide.Bottom => new RectInt32(this._panelX, this._hostHeight - this._hintThickness,
                    this._panelWidth, this._hintThickness),
                _ => new RectInt32()
            };
        }

        private void SetClickThrough(bool clickThrough)
        {
            if (this._isClickThrough == clickThrough)
            {
                return;
            }

            var style = GetExtendedWindowStyle(this._windowHandle);
            var passiveStyles = TransparentExtendedStyle | NoActivateExtendedStyle;
            var updatedStyle = clickThrough
                ? style | passiveStyles
                : style & ~passiveStyles;
            if (updatedStyle != style)
            {
                SetExtendedWindowStyle(this._windowHandle, updatedStyle);
            }

            this._isClickThrough = clickThrough;
        }

        private void OnCollapseRequested(object? sender, EventArgs args) =>
            this.CollapseRequested?.Invoke(this, EventArgs.Empty);

        private void OnPointerInteractionStarted(object? sender, EventArgs args) => this.NoteInteraction();

        private void OnPointerInteractionEnded(object? sender, EventArgs args) => this.NoteInteraction();

        private void OnExternalDragEntered(object? sender, EventArgs args)
        {
            this.IsActualDragOver = true;
            this.NoteInteraction();
            this.Reveal(false, false);
        }

        private void OnExternalDragLeft(object? sender, EventArgs args)
        {
            this.IsActualDragOver = false;
            this.NoteInteraction();
        }

        private void OnDropCompleted(object? sender, EventArgs args)
        {
            this.IsActualDragOver = false;
            this.NoteInteraction();
        }

        private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
        {
            this.IsWindowActive = args.WindowActivationState != WindowActivationState.Deactivated;
            if (this.IsWindowActive)
            {
                this.NoteInteraction();
            }
        }

        private void OnWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
        {
            if (this._isClosing)
            {
                return;
            }

            args.Cancel = true;
            this.Hide(false);
        }

        private static nint GetExtendedWindowStyle(nint windowHandle) => IntPtr.Size == 8
            ? GetWindowLongPtr64(windowHandle, ExtendedWindowStyle)
            : GetWindowLong32(windowHandle, ExtendedWindowStyle);

        private static void SetExtendedWindowStyle(nint windowHandle, nint style)
        {
            if (IntPtr.Size == 8)
            {
                SetWindowLongPtr64(windowHandle, ExtendedWindowStyle, style);
            }
            else
            {
                SetWindowLong32(windowHandle, ExtendedWindowStyle, (int)style);
            }
        }

        [LibraryImport("user32.dll")]
        private static partial uint GetDpiForWindow(nint windowHandle);

        [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        private static partial nint GetWindowLongPtr64(nint windowHandle, int index);

        [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        private static partial nint SetWindowLongPtr64(nint windowHandle, int index, nint value);

        [LibraryImport("user32.dll", EntryPoint = "GetWindowLongW")]
        private static partial int GetWindowLong32(nint windowHandle, int index);

        [LibraryImport("user32.dll", EntryPoint = "SetWindowLongW")]
        private static partial int SetWindowLong32(nint windowHandle, int index, int value);
    }
}
