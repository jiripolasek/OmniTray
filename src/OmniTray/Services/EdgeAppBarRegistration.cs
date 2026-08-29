// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

using System.Runtime.InteropServices;
using Windows.Graphics;

namespace OmniTray.Services;

/// <summary>
///     Registers an edge shelf HWND with the Windows shell as a pinned AppBar.
/// </summary>
internal sealed partial class EdgeAppBarRegistration : IDisposable
{
    private const int WindowProcedureIndex = -4;
    private const uint AppBarMessageNew = 0;
    private const uint AppBarMessageRemove = 1;
    private const uint AppBarMessageQueryPosition = 2;
    private const uint AppBarMessageSetPosition = 3;
    private const uint AppBarNotificationPositionChanged = 1;
    private const uint AppBarNotificationFullScreenApp = 2;
    private const uint AppBarEdgeLeft = 0;
    private const uint AppBarEdgeTop = 1;
    private const uint AppBarEdgeRight = 2;
    private const uint AppBarEdgeBottom = 3;
    private const nint WindowBottom = 1;
    private const nint WindowTopmost = -1;
    private const uint SetWindowPositionNoSize = 0x0001;
    private const uint SetWindowPositionNoMove = 0x0002;
    private const uint SetWindowPositionNoActivate = 0x0010;

    private readonly Action<bool> _fullScreenChanged;
    private readonly nint _hwnd;
    private readonly Action _positionChanged;
    private readonly Action _shellRestarted;
    private readonly WindowProcedure _windowProcedure;
    private readonly nint _windowProcedurePointer;
    private AppBarData _appBarData;
    private bool _isDisposed;
    private nint _originalWindowProcedure;

    public bool IsRegistered { get; private set; }

    private uint CallbackMessage { get; }

    private uint TaskbarRestartMessage { get; }

    public EdgeAppBarRegistration(
        nint hwnd,
        Action positionChanged,
        Action shellRestarted,
        Action<bool> fullScreenChanged)
    {
        if (hwnd == 0)
        {
            throw new ArgumentException("An AppBar requires a valid window handle.", nameof(hwnd));
        }

        this._hwnd = hwnd;
        this._positionChanged = positionChanged ?? throw new ArgumentNullException(nameof(positionChanged));
        this._shellRestarted = shellRestarted ?? throw new ArgumentNullException(nameof(shellRestarted));
        this._fullScreenChanged = fullScreenChanged ?? throw new ArgumentNullException(nameof(fullScreenChanged));
        this.CallbackMessage = RegisterWindowMessage($"OmniTray.EdgeAppBar.{hwnd}");
        this.TaskbarRestartMessage = RegisterWindowMessage("TaskbarCreated");

        this._windowProcedure = this.CustomWindowProcedure;
        this._windowProcedurePointer = Marshal.GetFunctionPointerForDelegate(this._windowProcedure);
        this._originalWindowProcedure = SetWindowProcedure(this._hwnd, this._windowProcedurePointer);
    }

    public bool Register()
    {
        if (this._isDisposed || this.IsRegistered || this.CallbackMessage == 0)
        {
            return this.IsRegistered;
        }

        this._appBarData = new AppBarData
        {
            Size = (uint)Marshal.SizeOf<AppBarData>(),
            WindowHandle = this._hwnd,
            CallbackMessage = this.CallbackMessage
        };
        this.IsRegistered = ShellAppBarMessage(AppBarMessageNew, ref this._appBarData) != 0;
        if (!this.IsRegistered)
        {
            this._appBarData = default;
        }

        return this.IsRegistered;
    }

    public void Unregister()
    {
        if (!this.IsRegistered)
        {
            return;
        }

        this.IsRegistered = false;
        _ = ShellAppBarMessage(AppBarMessageRemove, ref this._appBarData);
        this._appBarData = default;
    }

    public RectInt32 UpdatePosition(EdgeShelfSide side, RectInt32 monitorBounds, int thickness)
    {
        if (!this.IsRegistered)
        {
            return default;
        }

        thickness = Math.Max(1, thickness);
        this._appBarData.Edge = side switch
        {
            EdgeShelfSide.Left => AppBarEdgeLeft,
            EdgeShelfSide.Top => AppBarEdgeTop,
            EdgeShelfSide.Right => AppBarEdgeRight,
            EdgeShelfSide.Bottom => AppBarEdgeBottom,
            _ => throw new ArgumentOutOfRangeException(nameof(side))
        };
        this._appBarData.Rectangle = CreateRequestedRectangle(side, monitorBounds, thickness);

        _ = ShellAppBarMessage(AppBarMessageQueryPosition, ref this._appBarData);
        ApplyRequestedThickness(ref this._appBarData.Rectangle, side, thickness);
        _ = ShellAppBarMessage(AppBarMessageSetPosition, ref this._appBarData);

        var rectangle = this._appBarData.Rectangle;
        _ = MoveWindow(
            this._hwnd,
            rectangle.Left,
            rectangle.Top,
            rectangle.Right - rectangle.Left,
            rectangle.Bottom - rectangle.Top,
            true);
        return new RectInt32(
            rectangle.Left,
            rectangle.Top,
            rectangle.Right - rectangle.Left,
            rectangle.Bottom - rectangle.Top);
    }

    public void SetFullScreenState(bool fullScreenAppOpen)
    {
        if (!this.IsRegistered)
        {
            return;
        }

        _ = SetWindowPosition(
            this._hwnd,
            fullScreenAppOpen ? WindowBottom : WindowTopmost,
            0,
            0,
            0,
            0,
            SetWindowPositionNoMove | SetWindowPositionNoSize | SetWindowPositionNoActivate);
    }

    public void Dispose()
    {
        if (this._isDisposed)
        {
            return;
        }

        this._isDisposed = true;
        this.Unregister();

        if (this._originalWindowProcedure != 0 &&
            GetWindowProcedure(this._hwnd) == this._windowProcedurePointer)
        {
            _ = SetWindowProcedure(this._hwnd, this._originalWindowProcedure);
        }

        this._originalWindowProcedure = 0;
    }

    private static NativeRect CreateRequestedRectangle(
        EdgeShelfSide side,
        RectInt32 monitorBounds,
        int thickness)
    {
        var rectangle = new NativeRect
        {
            Left = monitorBounds.X,
            Top = monitorBounds.Y,
            Right = monitorBounds.X + monitorBounds.Width,
            Bottom = monitorBounds.Y + monitorBounds.Height
        };
        ApplyRequestedThickness(ref rectangle, side, thickness);
        return rectangle;
    }

    private static void ApplyRequestedThickness(
        ref NativeRect rectangle,
        EdgeShelfSide side,
        int thickness)
    {
        switch (side)
        {
            case EdgeShelfSide.Left:
                rectangle.Right = rectangle.Left + thickness;
                break;

            case EdgeShelfSide.Right:
                rectangle.Left = rectangle.Right - thickness;
                break;

            case EdgeShelfSide.Top:
                rectangle.Bottom = rectangle.Top + thickness;
                break;

            case EdgeShelfSide.Bottom:
                rectangle.Top = rectangle.Bottom - thickness;
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(side));
        }
    }

    private nint CustomWindowProcedure(nint hwnd, uint message, nuint wParam, nint lParam)
    {
        if (!this._isDisposed && message == this.CallbackMessage)
        {
            if (wParam == AppBarNotificationPositionChanged)
            {
                this._positionChanged();
            }
            else if (wParam == AppBarNotificationFullScreenApp)
            {
                this._fullScreenChanged(lParam != 0);
            }
        }
        else if (!this._isDisposed && message == this.TaskbarRestartMessage)
        {
            // Explorer lost the registration when its shell window restarted.
            this.IsRegistered = false;
            this._appBarData = default;
            this._shellRestarted();
        }

        return this._originalWindowProcedure == 0
            ? 0
            : CallWindowProcedure(this._originalWindowProcedure, hwnd, message, wParam, lParam);
    }

    private static nint GetWindowProcedure(nint hwnd) => IntPtr.Size == 8
        ? GetWindowLongPointer64(hwnd, WindowProcedureIndex)
        : GetWindowLong32(hwnd, WindowProcedureIndex);

    private static nint SetWindowProcedure(nint hwnd, nint value) => IntPtr.Size == 8
        ? SetWindowLongPointer64(hwnd, WindowProcedureIndex, value)
        : SetWindowLong32(hwnd, WindowProcedureIndex, (int)value);

    [LibraryImport("shell32.dll", EntryPoint = "SHAppBarMessage")]
    private static partial nuint ShellAppBarMessage(uint message, ref AppBarData data);

    [LibraryImport("user32.dll", EntryPoint = "RegisterWindowMessageW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint RegisterWindowMessage(string message);

    [LibraryImport("user32.dll", EntryPoint = "MoveWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool MoveWindow(
        nint hwnd,
        int x,
        int y,
        int width,
        int height,
        [MarshalAs(UnmanagedType.Bool)] bool repaint);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowPos")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPosition(
        nint hwnd,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [LibraryImport("user32.dll", EntryPoint = "CallWindowProcW")]
    private static partial nint CallWindowProcedure(
        nint previousWindowProcedure,
        nint hwnd,
        uint message,
        nuint wParam,
        nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static partial nint GetWindowLongPointer64(nint hwnd, int index);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static partial nint SetWindowLongPointer64(nint hwnd, int index, nint value);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static partial int GetWindowLong32(nint hwnd, int index);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static partial int SetWindowLong32(nint hwnd, int index, int value);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint WindowProcedure(nint hwnd, uint message, nuint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct AppBarData
    {
        public uint Size;

        public nint WindowHandle;

        public uint CallbackMessage;

        public uint Edge;

        public NativeRect Rectangle;

        public nint Parameter;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;

        public int Top;

        public int Right;

        public int Bottom;
    }
}
