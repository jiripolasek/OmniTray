// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

using Windows.Graphics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;

namespace OmniTray.Services;

internal sealed class TrayWindowSession
{
    public event EventHandler? Closed;

    public event EventHandler? StateChanged;
    private readonly TrayWindowContentFactory _contentFactory;
    private readonly TrayContentViewModel _viewModel;
    private Window? _activeWindow;
    private bool _isChangingPresentation;
    private bool _isClosed;
    private bool _isClosing;
    private SizeInt32 _normalSize;

    public Window ActiveWindow =>
        this._activeWindow ?? throw new InvalidOperationException("The tray window is closed.");

    public bool IsMinimalMode { get; private set; }

    public SizeInt32 NormalSize => this._normalSize;

    public TrayWindowSession(
        TrayContentViewModel viewModel,
        TrayWindowContentFactory contentFactory,
        bool isMinimal = false)
    {
        this._viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        this._contentFactory = contentFactory ?? throw new ArgumentNullException(nameof(contentFactory));
        this.IsMinimalMode = isMinimal;
        this._activeWindow = this.CreateWindow(isMinimal);
        this._normalSize = new SizeInt32(
            WindowCoordinator.DipsToPixels(this._activeWindow, TrayWindow.DefaultWidthInDips),
            WindowCoordinator.DipsToPixels(this._activeWindow, TrayWindow.DefaultHeightInDips));
    }

    public void Activate()
    {
        if (!this._isClosed && !this._isClosing)
        {
            this.ActiveWindow.Activate();
        }
    }

    public void Close()
    {
        if (this._isClosing)
        {
            return;
        }

        this._isClosing = true;
        if (this._isChangingPresentation)
        {
            return;
        }

        var window = this._activeWindow;
        if (window is not null)
        {
            PrepareWindowForClose(window, () => this.ClosePreparedWindow(window));
            return;
        }

        this.CompleteSessionClose();
    }

    public void MoveAndResizeActive(RectInt32 bounds)
    {
        this.ActiveWindow.AppWindow.MoveAndResize(bounds);
        if (!this.IsMinimalMode)
        {
            this._normalSize = new SizeInt32(bounds.Width, bounds.Height);
        }
    }

    public void RestoreNormalSize(SizeInt32 normalSize)
    {
        if (IsUsableSize(normalSize))
        {
            this._normalSize = normalSize;
        }
    }

    private void OnMinimalModeRequested(object? sender, EventArgs args) =>
        this.QueuePresentationChange(true);

    private void OnExpandRequested(object? sender, EventArgs args) =>
        this.QueuePresentationChange(false);

    private void OnCloseRequested(object? sender, EventArgs args)
    {
        if (this._isClosed || this._isClosing)
        {
            return;
        }

        this.ActiveWindow.DispatcherQueue.TryEnqueue(
            DispatcherQueuePriority.Low,
            this.Close);
    }

    private void QueuePresentationChange(bool isMinimal, Action? completed = null)
    {
        if (this._isClosed || this._isClosing)
        {
            return;
        }

        var dispatcherQueue = this.ActiveWindow.DispatcherQueue;
        dispatcherQueue.TryEnqueue(
            DispatcherQueuePriority.Low,
            () =>
            {
                if (this._isClosed || this._isClosing)
                {
                    return;
                }

                if (this._isChangingPresentation)
                {
                    return;
                }

                if (this.IsMinimalMode != isMinimal)
                {
                    this.ReplaceWindow(isMinimal, completed);
                    return;
                }

                completed?.Invoke();
            });
    }

    private void ReplaceWindow(bool isMinimal, Action? completed)
    {
        this._isChangingPresentation = true;
        var outgoingWindow = this.ActiveWindow;
        var position = outgoingWindow.AppWindow.Position;
        if (!this.IsMinimalMode)
        {
            var normalSize = outgoingWindow.AppWindow.Size;
            if (IsUsableSize(normalSize))
            {
                this._normalSize = normalSize;
            }
        }

        PrepareWindowForClose(
            outgoingWindow,
            () => this.CompleteWindowReplacement(
                outgoingWindow,
                position,
                isMinimal,
                completed));
    }

    private void CompleteWindowReplacement(
        Window outgoingWindow,
        PointInt32 position,
        bool isMinimal,
        Action? completed)
    {
        if (this._isClosed || !ReferenceEquals(outgoingWindow, this._activeWindow))
        {
            this._isChangingPresentation = false;
            return;
        }

        if (this._isClosing)
        {
            this._isChangingPresentation = false;
            this.ClosePreparedWindow(outgoingWindow);
            return;
        }

        this.DetachWindow(outgoingWindow);
        this._activeWindow = null;
        outgoingWindow.Close();
        if (this._isClosing)
        {
            this._isChangingPresentation = false;
            this.CompleteSessionClose();
            return;
        }

        this.IsMinimalMode = isMinimal;
        var incomingWindow = this.CreateWindow(isMinimal);
        this._activeWindow = incomingWindow;
        incomingWindow.AppWindow.Move(position);
        var requestedSize = isMinimal
            ? new SizeInt32(
                WindowCoordinator.DipsToPixels(incomingWindow, MinimalTrayWindow.DefaultSizeInDips),
                WindowCoordinator.DipsToPixels(incomingWindow, MinimalTrayWindow.DefaultSizeInDips))
            : this._normalSize;
        MoveAndResizeWithinWorkArea(incomingWindow, position, requestedSize);
        incomingWindow.Activate();
        this._isChangingPresentation = false;
        this.StateChanged?.Invoke(this, EventArgs.Empty);
        completed?.Invoke();
    }

    private void ClosePreparedWindow(Window window)
    {
        if (this._isClosed || !ReferenceEquals(window, this._activeWindow))
        {
            return;
        }

        this.DetachWindow(window);
        this._activeWindow = null;
        window.Close();
        this.CompleteSessionClose();
    }

    private void CompleteSessionClose()
    {
        if (this._isClosed)
        {
            return;
        }

        this._isChangingPresentation = false;
        this._isClosed = true;
        this._viewModel.Dispose();
        this.Closed?.Invoke(this, EventArgs.Empty);
    }

    private static void PrepareWindowForClose(Window window, Action completed)
    {
        switch (window)
        {
            case TrayWindow normalWindow:
                normalWindow.PrepareForClose(completed);
                break;
            case MinimalTrayWindow minimalWindow:
                minimalWindow.PrepareForClose(completed);
                break;
            default:
                if (!window.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => completed()))
                {
                    completed();
                }

                break;
        }
    }

    private Window CreateWindow(bool isMinimal)
    {
        if (isMinimal)
        {
            var window = new MinimalTrayWindow(this._viewModel, this._contentFactory);
            window.ExpandRequested += this.OnExpandRequested;
            window.CloseRequested += this.OnCloseRequested;
            window.AppWindow.Changed += this.OnWindowChanged;
            window.Closed += this.OnWindowClosed;
            return window;
        }

        var normalWindow = new TrayWindow(this._viewModel, this._contentFactory);
        normalWindow.MinimalModeRequested += this.OnMinimalModeRequested;
        normalWindow.CloseRequested += this.OnCloseRequested;
        normalWindow.AppWindow.Changed += this.OnWindowChanged;
        normalWindow.Closed += this.OnWindowClosed;
        return normalWindow;
    }

    private void DetachWindow(Window window)
    {
        switch (window)
        {
            case MinimalTrayWindow minimalWindow:
                minimalWindow.ExpandRequested -= this.OnExpandRequested;
                minimalWindow.CloseRequested -= this.OnCloseRequested;
                break;
            case TrayWindow normalWindow:
                normalWindow.MinimalModeRequested -= this.OnMinimalModeRequested;
                normalWindow.CloseRequested -= this.OnCloseRequested;
                break;
        }

        window.AppWindow.Changed -= this.OnWindowChanged;
        window.Closed -= this.OnWindowClosed;
    }

    private void OnWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (this._isClosing || this._isClosed)
        {
            return;
        }

        if (!this.IsMinimalMode && args.DidSizeChange && IsUsableSize(sender.Size))
        {
            this._normalSize = sender.Size;
        }

        if (args.DidPositionChange || args.DidSizeChange)
        {
            this.StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        if (this._isClosed || !ReferenceEquals(sender, this._activeWindow))
        {
            return;
        }

        this._activeWindow = null;
        this._isClosing = true;
        this._isChangingPresentation = false;
        this.CompleteSessionClose();
    }

    private static void MoveAndResizeWithinWorkArea(
        Window window,
        PointInt32 position,
        SizeInt32 requestedSize)
    {
        var requested = new RectInt32(
            position.X,
            position.Y,
            Math.Max(requestedSize.Width, 1),
            Math.Max(requestedSize.Height, 1));
        var workArea = DisplayArea.GetFromRect(requested, DisplayAreaFallback.Nearest).WorkArea;
        var width = Math.Min(requested.Width, workArea.Width);
        var height = Math.Min(requested.Height, workArea.Height);
        var maximumX = workArea.X + workArea.Width - width;
        var maximumY = workArea.Y + workArea.Height - height;
        window.AppWindow.MoveAndResize(new RectInt32(
            Math.Clamp(position.X, workArea.X, maximumX),
            Math.Clamp(position.Y, workArea.Y, maximumY),
            width,
            height));
    }

    private static bool IsUsableSize(SizeInt32 size) => size.Width > 0 && size.Height > 0;
}
