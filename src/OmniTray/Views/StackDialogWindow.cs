// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using WinRT.Interop;

namespace OmniTray.Views;

internal sealed partial class StackDialogWindow : TransparentWindow
{
    private const int DefaultHeightInDips = 272;
    private const int DefaultWidthInDips = 520;
    private const int GwlpHwndParent = -8;

    private readonly Grid _root = new();
    private readonly nint _ownerHandle;
    private bool _isClosed;
    private bool _isOwnerDisabled;

    private StackDialogWindow(Window owner, string title)
        : base(false)
    {
        ArgumentNullException.ThrowIfNull(owner);

        this.Title = title;
        this.Content = this._root;
        if (this.AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = true;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsResizable = false;
        }

        this._ownerHandle = WindowNative.GetWindowHandle(owner);
        var dialogHandle = WindowNative.GetWindowHandle(this);
        _ = SetWindowLongPtr(dialogHandle, GwlpHwndParent, this._ownerHandle);
        this.CenterOnOwnerDisplay(owner);
        this.Closed += this.OnClosed;
    }

    public static async Task<bool> ShowAsync(
        Window owner,
        string title,
        string content,
        string primaryButtonText)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(primaryButtonText);

        var window = new StackDialogWindow(owner, title);
        return await window.ShowCoreAsync(title, content, primaryButtonText);
    }

    private async Task<bool> ShowCoreAsync(
        string title,
        string content,
        string primaryButtonText)
    {
        var loaded = new TaskCompletionSource<XamlRoot>();
        void OnLoaded(object sender, RoutedEventArgs args)
        {
            this._root.Loaded -= OnLoaded;
            if (this._root.XamlRoot is { } xamlRoot)
            {
                loaded.TrySetResult(xamlRoot);
            }
        }

        void OnWindowClosed(object sender, WindowEventArgs args) => loaded.TrySetCanceled();

        this._root.Loaded += OnLoaded;
        this.Closed += OnWindowClosed;
        this.DisableOwner();
        this.Activate();
        try
        {
            var xamlRoot = this._root.XamlRoot ?? await loaded.Task;
            var dialog = new ContentDialog
            {
                XamlRoot = xamlRoot,
                Title = title,
                Content = content,
                PrimaryButtonText = primaryButtonText,
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close
            };

            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }
        catch (Exception) when (this._isClosed)
        {
            return false;
        }
        finally
        {
            this._root.Loaded -= OnLoaded;
            this.Closed -= OnWindowClosed;
            if (!this._isClosed)
            {
                this.Close();
            }

            this.RestoreOwner();
        }
    }

    private void CenterOnOwnerDisplay(Window owner)
    {
        var width = WindowCoordinator.DipsToPixels(this, DefaultWidthInDips);
        var height = WindowCoordinator.DipsToPixels(this, DefaultHeightInDips);
        var workArea = DisplayArea.GetFromWindowId(owner.AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        this.AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(
            workArea.X + ((workArea.Width - width) / 2),
            workArea.Y + ((workArea.Height - height) / 2),
            width,
            height));
    }

    private void DisableOwner()
    {
        if (this._ownerHandle != 0)
        {
            _ = EnableWindow(this._ownerHandle, false);
            this._isOwnerDisabled = true;
        }
    }

    private void RestoreOwner()
    {
        if (!this._isOwnerDisabled)
        {
            return;
        }

        this._isOwnerDisabled = false;
        _ = EnableWindow(this._ownerHandle, true);
        _ = SetForegroundWindow(this._ownerHandle);
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        this._isClosed = true;
        this.RestoreOwner();
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnableWindow(nint windowHandle, [MarshalAs(UnmanagedType.Bool)] bool enable);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static partial nint SetWindowLongPtr(nint windowHandle, int index, nint newValue);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint windowHandle);
}
