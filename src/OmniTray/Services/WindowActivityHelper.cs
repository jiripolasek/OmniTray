// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace OmniTray.Services;

internal static partial class WindowActivityHelper
{
    internal static readonly KnownTriggerApp[] KnownTriggerWindowClasses =
    [
        new("NVIDIA overlay", "CEF-OSC-WIDGET", "NVIDIA Overlay")
    ];

    public static UserNotificationState? GetUserNotificationState() =>
        SHQueryUserNotificationState(out var state) >= 0 ? state : null;

    internal static UserNotificationFlags GetUserNotificationFlags() =>
        GetUserNotificationFlags(GetUserNotificationState());

    internal static UserNotificationFlags GetUserNotificationFlags(UserNotificationState? state) => new(
        state,
        state is UserNotificationState.RunningD3DFullScreen,
        state is UserNotificationState.PresentationMode,
        state is UserNotificationState.Busy);

    public static unsafe IReadOnlyList<string> FindVisibleTriggerApps()
    {
        var detected = new HashSet<string>(StringComparer.Ordinal);
        var detectedHandle = GCHandle.Alloc(detected);
        try
        {
            EnumWindows(&InspectWindow, GCHandle.ToIntPtr(detectedHandle));
        }
        finally
        {
            detectedHandle.Free();
        }

        return [.. detected];
    }

    public static string GetStateDisplayName(UserNotificationState? state) => state switch
    {
        UserNotificationState.RunningD3DFullScreen => "a fullscreen Direct3D app",
        UserNotificationState.PresentationMode => "Windows presentation mode",
        UserNotificationState.Busy => "the Windows busy state",
        _ => "no interrupt-suppression state"
    };

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int InspectWindow(nint windowHandle, nint context)
    {
        try
        {
            if (IsWindowVisible(windowHandle) == 0)
            {
                return 1;
            }

            var className = GetWindowClassName(windowHandle);
            if (className is null)
            {
                return 1;
            }

            var detected = GCHandle.FromIntPtr(context).Target as HashSet<string>;
            if (detected is null)
            {
                return 1;
            }

            foreach (var app in KnownTriggerWindowClasses)
            {
                if (!string.Equals(className, app.WindowClassName, StringComparison.OrdinalIgnoreCase) ||
                    GetWindowThreadProcessId(windowHandle, out var processId) == 0)
                {
                    continue;
                }

                try
                {
                    using var process = Process.GetProcessById((int)processId);
                    if (string.Equals(process.ProcessName, app.ProcessName, StringComparison.OrdinalIgnoreCase))
                    {
                        detected.Add(app.DisplayName);
                    }
                }
                catch
                {
                    // The process may exit between window enumeration and lookup.
                }
            }
        }
        catch
        {
            // Never allow a managed exception to cross the unmanaged callback boundary.
        }

        return 1;
    }

    private static unsafe string? GetWindowClassName(nint windowHandle)
    {
        const int maximumLength = 256;
        var buffer = stackalloc char[maximumLength];
        var length = GetClassName(windowHandle, buffer, maximumLength);
        return length > 0 ? new string(buffer, 0, length) : null;
    }

    [LibraryImport("shell32.dll")]
    private static partial int SHQueryUserNotificationState(out UserNotificationState state);

    [LibraryImport("user32.dll")]
    private static unsafe partial int EnumWindows(
        delegate* unmanaged[Stdcall]<nint, nint, int> callback,
        nint context);

    [LibraryImport("user32.dll")]
    private static partial int IsWindowVisible(nint windowHandle);

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint windowHandle, out uint processId);

    [LibraryImport("user32.dll", EntryPoint = "GetClassNameW")]
    private static unsafe partial int GetClassName(
        nint windowHandle,
        char* className,
        int maximumCount);

    internal sealed record KnownTriggerApp(
        string ProcessName,
        string WindowClassName,
        string DisplayName);

    internal enum UserNotificationState
    {
        NotPresent = 1,
        Busy = 2,
        RunningD3DFullScreen = 3,
        PresentationMode = 4,
        AcceptsNotifications = 5,
        QuietTime = 6,
        App = 7
    }

    internal readonly record struct UserNotificationFlags(
        UserNotificationState? State,
        bool IsRunningD3DFullScreen,
        bool IsPresentationMode,
        bool IsBusy)
    {
        public bool IsFullscreenState => this.IsRunningD3DFullScreen || this.IsPresentationMode;
    }
}
