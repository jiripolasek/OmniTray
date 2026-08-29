// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

using System.Runtime.InteropServices;
using Windows.Graphics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.Win32;
using OmniTray.Controls;
using WinRT.Interop;

namespace OmniTray.Services;

internal sealed partial class WindowCoordinator
{
    public event EventHandler? TrayWindowStatesChanged;

    public event EventHandler? DropCommandWindowStatesChanged;
    private const int PopupEdgeInsetInDips = 8;

    private readonly DropCommandCatalogViewModel _dropCommandCatalog;
    private readonly Dictionary<Guid, TrayWindowSession> _dropCommandWindows = [];
    private readonly EdgeWindowController _edgeWindowController;
    private readonly Dictionary<Guid, TrayWindowSession> _trayWindows = [];
    private readonly MainViewModel _viewModel;
    private DataFormatInspectorWindow? _dataFormatInspectorWindow;
    private bool _isClosing;
    private bool _isPopupVisible;
    private OmniTrayPopupWindow? _popupWindow;
    private SettingsWindow? _settingsWindow;
    private StackOrganizerWindow? _stackOrganizerWindow;
    private ToastWindow? _toastWindow;

    public WindowCoordinator(
        MainViewModel viewModel,
        DropCommandCatalogViewModel dropCommandCatalog,
        DispatcherQueue dispatcherQueue,
        AppSettingsService appSettingsService,
        Func<bool> isShakeToCreateTrayEnabled)
    {
        this._viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        this._dropCommandCatalog = dropCommandCatalog ??
                                   throw new ArgumentNullException(nameof(dropCommandCatalog));
        this._edgeWindowController = new EdgeWindowController(
            this._viewModel,
            dispatcherQueue,
            appSettingsService,
            isShakeToCreateTrayEnabled,
            this.OnShakeToCreateTray);
    }

    public void TogglePopup()
    {
        if (this._isPopupVisible)
        {
            this.HidePopup();
        }
        else
        {
            this.ShowPopup();
        }
    }

    public void ShowPopup()
    {
        this._popupWindow ??= this.CreatePopupWindow();
        PositionPopupWindow(this._popupWindow);
        this._popupWindow.Activate();
        this._isPopupVisible = true;
    }

    public void ShowTray(DropStackViewModel stack) => this.ShowTray(stack, null, true);

    private void ShowTray(DropStackViewModel stack, PointInt32? pointer, bool hidePopup)
    {
        ArgumentNullException.ThrowIfNull(stack);
        if (hidePopup)
        {
            this.HidePopup();
        }

        if (this._trayWindows.TryGetValue(stack.Model.Id, out var existingSession))
        {
            if (pointer is { } existingPointer)
            {
                PositionTrayWindowAtPointer(existingSession, existingPointer, true);
            }

            existingSession.Activate();
            return;
        }

        var session = this.CreateTrayWindowSession(stack);
        var placementSlot = this._trayWindows.Count + this._dropCommandWindows.Count;
        this._trayWindows.Add(stack.Model.Id, session);
        if (pointer is { } targetPointer)
        {
            PositionTrayWindowAtPointer(session, targetPointer, false);
        }
        else
        {
            PositionTrayWindow(session.ActiveWindow, placementSlot);
        }

        session.Activate();
        this.TrayWindowStatesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ShowDropCommand(DropCommandViewModel command)
    {
        ArgumentNullException.ThrowIfNull(command);
        this.HidePopup();
        if (this._dropCommandWindows.TryGetValue(command.Id, out var existing))
        {
            existing.Activate();
            return;
        }

        var session = this.CreateDropCommandWindowSession(command);
        var placementSlot = this._trayWindows.Count + this._dropCommandWindows.Count;
        this._dropCommandWindows.Add(command.Id, session);
        PositionTrayWindow(session.ActiveWindow, placementSlot);
        session.Activate();
        this.DropCommandWindowStatesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RestoreDropCommandWindows(
        IEnumerable<DropCommandWindowState> states,
        Func<Guid, DropCommandViewModel?> resolveCommand)
    {
        ArgumentNullException.ThrowIfNull(states);
        ArgumentNullException.ThrowIfNull(resolveCommand);
        var restoredAny = false;
        foreach (var state in states
                     .GroupBy(static state => state.CommandId)
                     .Select(static group => group.Last()))
        {
            var command = resolveCommand(state.CommandId);
            if (command is null || this._dropCommandWindows.ContainsKey(state.CommandId))
            {
                continue;
            }

            var session = this.CreateDropCommandWindowSession(command, state.IsMinimal);
            this._dropCommandWindows.Add(command.Id, session);
            session.RestoreNormalSize(ResolveNormalSize(session.ActiveWindow, state));
            PositionTrayWindow(session.ActiveWindow, state);
            session.Activate();
            restoredAny = true;
        }

        if (restoredAny)
        {
            this.DropCommandWindowStatesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public IReadOnlyList<DropCommandWindowState> GetOpenDropCommandWindowStates() =>
        this._dropCommandWindows.Select(static pair =>
        {
            var activeWindow = pair.Value.ActiveWindow;
            var position = activeWindow.AppWindow.Position;
            var size = activeWindow.AppWindow.Size;
            var normalSize = pair.Value.NormalSize;
            if (normalSize.Width <= 0 || normalSize.Height <= 0)
            {
                normalSize = pair.Value.IsMinimalMode
                    ? new SizeInt32(
                        DipsToPixels(activeWindow, TrayWindow.DefaultWidthInDips),
                        DipsToPixels(activeWindow, TrayWindow.DefaultHeightInDips))
                    : size;
            }

            return new DropCommandWindowState(
                pair.Key,
                position.X,
                position.Y,
                size.Width,
                size.Height,
                pair.Value.IsMinimalMode,
                normalSize.Width,
                normalSize.Height);
        }).ToArray();

    public void RestoreTrays(
        IEnumerable<TrayWindowState> states,
        Func<Guid, DropStackViewModel?> resolveStack)
    {
        ArgumentNullException.ThrowIfNull(states);
        ArgumentNullException.ThrowIfNull(resolveStack);

        var restoredAny = false;
        foreach (var state in states
                     .GroupBy(static state => state.StackId)
                     .Select(static group => group.Last()))
        {
            var stack = resolveStack(state.StackId);
            if (stack is null || this._trayWindows.ContainsKey(state.StackId))
            {
                continue;
            }

            var session = this.CreateTrayWindowSession(stack, state.IsMinimal);
            this._trayWindows.Add(state.StackId, session);
            session.RestoreNormalSize(ResolveNormalSize(session.ActiveWindow, state));
            PositionTrayWindow(session.ActiveWindow, state);
            session.Activate();
            restoredAny = true;
        }

        if (restoredAny)
        {
            this.TrayWindowStatesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public IReadOnlyList<TrayWindowState> GetOpenTrayWindowStates() =>
        this._trayWindows
            .Select(static pair =>
            {
                var activeWindow = pair.Value.ActiveWindow;
                var position = activeWindow.AppWindow.Position;
                var size = activeWindow.AppWindow.Size;
                var normalSize = pair.Value.NormalSize;
                if (normalSize.Width <= 0 || normalSize.Height <= 0)
                {
                    normalSize = pair.Value.IsMinimalMode
                        ? new SizeInt32(
                            DipsToPixels(activeWindow, TrayWindow.DefaultWidthInDips),
                            DipsToPixels(activeWindow, TrayWindow.DefaultHeightInDips))
                        : size;
                }

                return new TrayWindowState(
                    pair.Key,
                    position.X,
                    position.Y,
                    size.Width,
                    size.Height,
                    pair.Value.IsMinimalMode,
                    normalSize.Width,
                    normalSize.Height);
            })
            .ToArray();

    public void ShowSettings()
    {
        this.HidePopup();
        this._settingsWindow ??= this.CreateSettingsWindow();
        CenterWindow(this._settingsWindow, 960, 680);
        this._settingsWindow.Activate();
    }

    public void ShowStackOrganizer(DropStackViewModel? stack = null)
    {
        this.HidePopup();
        this.GetStackOrganizerWindow().SelectStack(stack);
    }

    private StackOrganizerWindow GetStackOrganizerWindow()
    {
        if (this._stackOrganizerWindow is null)
        {
            this._stackOrganizerWindow = this.CreateStackOrganizerWindow();
            CenterWindow(this._stackOrganizerWindow, 1180, 760);
        }

        return this._stackOrganizerWindow;
    }

    public void ShowDataFormatInspector(DropItem? item = null)
    {
        this._dataFormatInspectorWindow ??= this.CreateDataFormatInspectorWindow();
        CenterWindow(this._dataFormatInspectorWindow, 1040, 720);
        if (item is not null)
        {
            this._dataFormatInspectorWindow.Inspect(item);
        }

        this._dataFormatInspectorWindow.Activate();
    }

    public void ShowEdgeShelf(EdgeShelfSide side = EdgeShelfSide.Right)
    {
        this.HidePopup();
        this._edgeWindowController.Show(side);
    }

    public void HideAllEdgeShelves() => this._edgeWindowController.HideAll();

    public void ShowToast(
        string message,
        InfoBarSeverity severity,
        ToastPosition positionPreference)
    {
        if (string.IsNullOrWhiteSpace(message) || this._isClosing)
        {
            return;
        }

        this._toastWindow ??= this.CreateToastWindow();
        var effectivePosition = PositionToastWindow(this._toastWindow, positionPreference);
        this._toastWindow.Present(message, severity, effectivePosition);
    }

    public void ReconcileTrays(IReadOnlySet<Guid> stackIds)
    {
        ArgumentNullException.ThrowIfNull(stackIds);
        foreach (var orphanedWindow in this._trayWindows
                     .Where(pair => !stackIds.Contains(pair.Key))
                     .Select(static pair => pair.Value)
                     .ToArray())
        {
            orphanedWindow.Close();
        }
    }

    public void ReconcileDropCommandWindows(IReadOnlySet<Guid> commandIds)
    {
        ArgumentNullException.ThrowIfNull(commandIds);
        foreach (var orphaned in this._dropCommandWindows
                     .Where(pair => !commandIds.Contains(pair.Key))
                     .Select(static pair => pair.Value)
                     .ToArray())
        {
            orphaned.Close();
        }
    }

    public void CloseAll()
    {
        this._isClosing = true;
        foreach (var window in this._noteWindows.Values.ToArray())
        {
            window.CloseDeleted();
        }

        this._edgeWindowController.Dispose();
        this._dataFormatInspectorWindow?.Close();
        this._settingsWindow?.Close();
        this._stackOrganizerWindow?.Close();
        this._popupWindow?.Close();
        this._toastWindow?.Close();
        foreach (var session in this._trayWindows.Values)
        {
            session.Close();
        }

        foreach (var session in this._dropCommandWindows.Values)
        {
            session.Close();
        }

        this._dataFormatInspectorWindow = null;
        this._settingsWindow = null;
        this._stackOrganizerWindow = null;
        this._popupWindow = null;
        this._toastWindow = null;
        this._trayWindows.Clear();
        this._dropCommandWindows.Clear();
    }

    public void HidePopup()
    {
        this._popupWindow?.CloseStackInspector();
        this._popupWindow?.AppWindow.Hide();
        this._isPopupVisible = false;
    }

    private OmniTrayPopupWindow CreatePopupWindow()
    {
        var window = new OmniTrayPopupWindow();
        window.AppWindow.IsShownInSwitchers = false;
        if (window.AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = true;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsResizable = false;
            presenter.SetBorderAndTitleBar(true, false);
        }

        window.AppWindow.Closing += (_, args) =>
        {
            if (this._isClosing)
            {
                return;
            }

            args.Cancel = true;
            window.CloseStackInspector();
            window.AppWindow.Hide();
            this._isPopupVisible = false;
        };
        return window;
    }

    private TrayWindowSession CreateTrayWindowSession(DropStackViewModel stack, bool isMinimal = false)
    {
        var trayViewModel = new StackTrayContentViewModel(stack);
        var session = new TrayWindowSession(
            trayViewModel,
            (owner, minimal) => new StackTrayContent(owner, stack, minimal),
            isMinimal);
        session.Closed += (_, _) =>
        {
            if (!this._isClosing)
            {
                this._trayWindows.Remove(stack.Model.Id);
                this.TrayWindowStatesChanged?.Invoke(this, EventArgs.Empty);
            }
        };
        session.StateChanged += (_, _) =>
        {
            if (!this._isClosing)
            {
                this.TrayWindowStatesChanged?.Invoke(this, EventArgs.Empty);
            }
        };
        return session;
    }

    private TrayWindowSession CreateDropCommandWindowSession(
        DropCommandViewModel command,
        bool isMinimal = false)
    {
        var trayViewModel = new DropCommandTrayContentViewModel(
            command,
            this._dropCommandCatalog.UpdateCommand);
        var session = new TrayWindowSession(
            trayViewModel,
            (owner, minimal) => new DropCommandTrayContent(owner, command, minimal),
            isMinimal);
        session.Closed += (_, _) =>
        {
            if (!this._isClosing)
            {
                this._dropCommandWindows.Remove(command.Id);
                this.DropCommandWindowStatesChanged?.Invoke(this, EventArgs.Empty);
            }
        };
        session.StateChanged += (_, _) =>
        {
            if (!this._isClosing)
            {
                this.DropCommandWindowStatesChanged?.Invoke(this, EventArgs.Empty);
            }
        };
        return session;
    }

    private SettingsWindow CreateSettingsWindow()
    {
        var window = new SettingsWindow();
        if (window.AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsMaximizable = true;
            presenter.IsMinimizable = true;
            presenter.IsResizable = true;
        }

        window.AppWindow.Closing += (_, args) =>
        {
            if (this._isClosing)
            {
                return;
            }

            args.Cancel = true;
            window.AppWindow.Hide();
        };
        return window;
    }

    private DataFormatInspectorWindow CreateDataFormatInspectorWindow()
    {
        var window = new DataFormatInspectorWindow();
        if (window.AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsMaximizable = true;
            presenter.IsMinimizable = true;
            presenter.IsResizable = true;
        }

        window.AppWindow.Closing += (_, args) =>
        {
            if (this._isClosing)
            {
                return;
            }

            args.Cancel = true;
            window.AppWindow.Hide();
        };
        return window;
    }

    private StackOrganizerWindow CreateStackOrganizerWindow()
    {
        var window = new StackOrganizerWindow();
        if (window.AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsMaximizable = true;
            presenter.IsMinimizable = true;
            presenter.IsResizable = true;
        }

        window.AppWindow.Closing += (_, args) =>
        {
            if (this._isClosing)
            {
                return;
            }

            args.Cancel = true;
            window.AppWindow.Hide();
        };
        return window;
    }

    private ToastWindow CreateToastWindow()
    {
        var window = new ToastWindow();
        if (window.AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = true;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsResizable = false;
        }

        window.AppWindow.Closing += (_, args) =>
        {
            if (this._isClosing)
            {
                return;
            }

            args.Cancel = true;
            window.AppWindow.Hide();
        };
        return window;
    }

    private static void PositionPopupWindow(Window window)
    {
        var width = DipsToPixels(window, OmniTrayPopupWindow.DefaultWidthInDips);
        var height = DipsToPixels(window, 640);
        var margin = DipsToPixels(window, PopupEdgeInsetInDips);
        var workArea = GetWorkArea(window);
        window.AppWindow.MoveAndResize(new RectInt32(
            workArea.X + workArea.Width - width - margin,
            workArea.Y + workArea.Height - height - margin,
            width,
            height));
    }

    private static ToastPosition PositionToastWindow(
        Window window,
        ToastPosition positionPreference)
    {
        var effectivePosition = ResolveToastPosition(positionPreference);
        var display = GetCursorPos(out var cursor)
            ? DisplayArea.GetFromPoint(
                new PointInt32(cursor.X, cursor.Y),
                DisplayAreaFallback.Primary)
            : DisplayArea.Primary;
        var workArea = display.WorkArea;

        // Moving first lets GetDpiForWindow observe the destination display's scale.
        window.AppWindow.Move(new PointInt32(workArea.X, workArea.Y));
        var width = Math.Min(DipsToPixels(window, 600), workArea.Width);
        var height = Math.Min(DipsToPixels(window, 180), workArea.Height);
        var x = effectivePosition == ToastPosition.TopLeft
            ? workArea.X
            : workArea.X + ((workArea.Width - width) / 2);
        var y = effectivePosition is ToastPosition.TopLeft or ToastPosition.TopCenter
            ? workArea.Y
            : workArea.Y + workArea.Height - height;
        window.AppWindow.MoveAndResize(new RectInt32(x, y, width, height));
        return effectivePosition;
    }

    private static ToastPosition ResolveToastPosition(ToastPosition positionPreference)
    {
        if (positionPreference != ToastPosition.UseSystemSettings)
        {
            return positionPreference;
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\SystemSettings\ConfirmatorPosition");
            return key?.GetValue("PositionIndex") is int positionIndex
                ? positionIndex switch
                {
                    2 => ToastPosition.TopLeft,
                    3 => ToastPosition.TopCenter,
                    _ => ToastPosition.BottomCenter
                }
                : ToastPosition.BottomCenter;
        }
        catch (Exception)
        {
            return ToastPosition.BottomCenter;
        }
    }

    private static void PositionTrayWindow(Window window, int slot)
    {
        var width = DipsToPixels(window, TrayWindow.DefaultWidthInDips);
        var height = DipsToPixels(window, TrayWindow.DefaultHeightInDips);
        var margin = DipsToPixels(window, 20);
        var gap = DipsToPixels(window, 12);
        var column = slot % 4;
        var row = slot / 4;
        var workArea = GetWorkArea(window);
        window.AppWindow.MoveAndResize(new RectInt32(
            workArea.X + workArea.Width - width - margin - (column * (width + gap)),
            workArea.Y + workArea.Height - height - margin - (row * (height + gap)),
            width,
            height));
    }

    private static void PositionTrayWindowAtPointer(
        TrayWindowSession session,
        PointInt32 pointer,
        bool preserveCurrentSize)
    {
        var window = session.ActiveWindow;
        var workArea = DisplayArea.GetFromPoint(pointer, DisplayAreaFallback.Nearest).WorkArea;

        // Moving first lets the window report the destination display's DPI before sizing it.
        window.AppWindow.Move(new PointInt32(workArea.X, workArea.Y));
        var requestedSize = preserveCurrentSize
            ? window.AppWindow.Size
            : new SizeInt32(
                DipsToPixels(window, TrayWindow.DefaultWidthInDips),
                DipsToPixels(window, TrayWindow.DefaultHeightInDips));
        var width = Math.Min(requestedSize.Width, workArea.Width);
        var height = Math.Min(requestedSize.Height, workArea.Height);
        var titleBarHeight = session.IsMinimalMode
            ? 0
            : Math.Min(DipsToPixels(window, 48), height / 2);
        var contentCenterY = titleBarHeight + ((height - titleBarHeight) / 2);
        var maximumX = workArea.X + workArea.Width - width;
        var maximumY = workArea.Y + workArea.Height - height;
        window.AppWindow.MoveAndResize(new RectInt32(
            Math.Clamp(pointer.X - (width / 2), workArea.X, maximumX),
            Math.Clamp(pointer.Y - contentCenterY, workArea.Y, maximumY),
            width,
            height));
    }

    private void OnShakeToCreateTray(PointInt32 pointer)
    {
        if (this._isClosing)
        {
            return;
        }

        var stack = DragDropDataService.ActiveStackReferenceId is { } stackId
            ? this._viewModel.Stacks.FirstOrDefault(candidate => candidate.Model.Id == stackId)
            : null;
        stack ??= this._viewModel.AddStack(DropStack.CreateEmpty());
        this.ShowTray(stack, pointer, false);
    }

    private static void PositionTrayWindow(Window window, TrayWindowState state) =>
        PositionTrayWindow(
            window,
            state.X,
            state.Y,
            state.Width,
            state.Height,
            state.IsMinimal);

    private static void PositionTrayWindow(Window window, DropCommandWindowState state) =>
        PositionTrayWindow(
            window,
            state.X,
            state.Y,
            state.Width,
            state.Height,
            state.IsMinimal);

    private static void PositionTrayWindow(
        Window window,
        int x,
        int y,
        int requestedWidth,
        int requestedHeight,
        bool isMinimal)
    {
        var defaultWidth = DipsToPixels(window, TrayWindow.DefaultWidthInDips);
        var defaultHeight = DipsToPixels(window, TrayWindow.DefaultHeightInDips);
        var minimumWidth = DipsToPixels(window, 120);
        var minimumHeight = DipsToPixels(window, 160);
        var defaultMinimalSize = DipsToPixels(window, MinimalTrayWindow.DefaultSizeInDips);
        var width = isMinimal
            ? defaultMinimalSize
            : requestedWidth >= minimumWidth && requestedWidth <= defaultWidth * 4
                ? requestedWidth
                : defaultWidth;
        var height = isMinimal
            ? defaultMinimalSize
            : requestedHeight >= minimumHeight && requestedHeight <= defaultHeight * 4
                ? requestedHeight
                : defaultHeight;

        var requested = new RectInt32(x, y, width, height);
        var workArea = DisplayArea.GetFromRect(requested, DisplayAreaFallback.Nearest).WorkArea;
        width = Math.Min(width, workArea.Width);
        height = Math.Min(height, workArea.Height);

        var maximumX = workArea.X + workArea.Width - width;
        var maximumY = workArea.Y + workArea.Height - height;
        window.AppWindow.MoveAndResize(new RectInt32(
            Math.Clamp(x, workArea.X, maximumX),
            Math.Clamp(y, workArea.Y, maximumY),
            width,
            height));
    }

    private static SizeInt32 ResolveNormalSize(Window window, TrayWindowState state) =>
        ResolveNormalSize(
            window,
            state.NormalWidth,
            state.NormalHeight,
            state.Width,
            state.Height,
            state.IsMinimal);

    private static SizeInt32 ResolveNormalSize(Window window, DropCommandWindowState state) =>
        ResolveNormalSize(
            window,
            state.NormalWidth,
            state.NormalHeight,
            state.Width,
            state.Height,
            state.IsMinimal);

    private static SizeInt32 ResolveNormalSize(
        Window window,
        int normalWidth,
        int normalHeight,
        int activeWidth,
        int activeHeight,
        bool isMinimal)
    {
        var defaultWidth = DipsToPixels(window, TrayWindow.DefaultWidthInDips);
        var defaultHeight = DipsToPixels(window, TrayWindow.DefaultHeightInDips);
        var minimumWidth = DipsToPixels(window, 120);
        var minimumHeight = DipsToPixels(window, 160);
        var width = normalWidth >= minimumWidth && normalWidth <= defaultWidth * 4
            ? normalWidth
            : !isMinimal && activeWidth >= minimumWidth && activeWidth <= defaultWidth * 4
                ? activeWidth
                : defaultWidth;
        var height = normalHeight >= minimumHeight && normalHeight <= defaultHeight * 4
            ? normalHeight
            : !isMinimal && activeHeight >= minimumHeight && activeHeight <= defaultHeight * 4
                ? activeHeight
                : defaultHeight;
        return new SizeInt32(width, height);
    }

    private static void CenterWindow(Window window, int widthInDips, int heightInDips)
    {
        var width = DipsToPixels(window, widthInDips);
        var height = DipsToPixels(window, heightInDips);
        var workArea = GetWorkArea(window);
        window.AppWindow.MoveAndResize(new RectInt32(
            workArea.X + ((workArea.Width - width) / 2),
            workArea.Y + ((workArea.Height - height) / 2),
            width,
            height));
    }

    private static RectInt32 GetWorkArea(Window window) =>
        DisplayArea.GetFromWindowId(window.AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;

    internal static int DipsToPixels(Window window, int dips)
    {
        var dpi = GetDpiForWindow(WindowNative.GetWindowHandle(window));
        var scale = dpi == 0 ? 1d : dpi / 96d;
        return (int)Math.Round(dips * scale);
    }

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(nint windowHandle);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out NativePoint point);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }
}
