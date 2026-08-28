// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.Graphics;

namespace OmniTray.Views;

public sealed partial class NoteWindow
{
    private InputNonClientPointerSource? _nonClientPointerSource;
    private bool _isWindowActive;
    private bool _isHeaderHovered;
    private bool _isCaptionHovered;
    private bool _isHeaderFlyoutOpen;
    private bool _chromeRefreshQueued;

    private void InitializeChrome()
    {
        this.ExtendsContentIntoTitleBar = true;
        this.SetTitleBar(this.AppTitleBar);
        // Hide the native caption controls without removing the resizable window frame.
        this.AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Collapsed;
        this._nonClientPointerSource = InputNonClientPointerSource.GetForWindowId(this.AppWindow.Id);
        this._nonClientPointerSource.PointerEntered += this.OnCaptionPointerEntered;
        this._nonClientPointerSource.PointerMoved += this.OnCaptionPointerEntered;
        this._nonClientPointerSource.PointerExited += this.OnCaptionPointerExited;
        this.Activated += this.OnWindowActivated;
    }

    private void UninitializeChrome()
    {
        if (this._nonClientPointerSource is { } source)
        {
            source.PointerEntered -= this.OnCaptionPointerEntered;
            source.PointerMoved -= this.OnCaptionPointerEntered;
            source.PointerExited -= this.OnCaptionPointerExited;
            this._nonClientPointerSource = null;
        }
        this.Activated -= this.OnWindowActivated;
        if (this.RootGrid.XamlRoot is { } root) { root.Changed -= this.OnXamlRootChanged; }
    }

    private void OnTitleBarSizeChanged(object sender, SizeChangedEventArgs args) => this.UpdateTitleBarInput();

    private void OnXamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => this.UpdateTitleBarInput();

    private void UpdateTitleBarInput()
    {
        if (this._isClosed || this._nonClientPointerSource is null || this.AppTitleBar.XamlRoot is not { } root)
        {
            return;
        }

        var scale = root.RasterizationScale;
        var bounds = this.HeaderActions.TransformToVisual(this.RootGrid).TransformBounds(
            new Rect(0, 0, this.HeaderActions.ActualWidth, this.HeaderActions.ActualHeight));
        var left = (int)Math.Floor(bounds.Left * scale);
        var top = (int)Math.Floor(bounds.Top * scale);
        // Keep these XAML hit targets out of the native drag region, even when the
        // buttons are transparent. Round the far edges out at fractional DPI too.
        this._nonClientPointerSource.SetRegionRects(NonClientRegionKind.Passthrough,
        [
            new RectInt32(left, top, (int)Math.Ceiling(bounds.Right * scale) - left,
                (int)Math.Ceiling(bounds.Bottom * scale) - top)
        ]);
    }

    private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        this._isWindowActive = args.WindowActivationState != WindowActivationState.Deactivated;
        if (!this._isWindowActive)
        {
            this._isHeaderHovered = false;
            this._isCaptionHovered = false;
        }
        this.QueueChromeRefresh();
    }

    private void OnChromeFocusChanged(object sender, RoutedEventArgs args) => this.QueueChromeRefresh();

    private void QueueChromeRefresh()
    {
        if (this._isClosed || this._chromeRefreshQueued) { return; }
        this._chromeRefreshQueued = true;
        // LostFocus precedes GotFocus. Wait for the new target so clicking a formatting
        // button cannot collapse the bar before the button receives its click.
        this.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            this._chromeRefreshQueued = false;
            if (this._isClosed) { return; }
            this.Editor.SetHostActive(this._isWindowActive);
            this.UpdateHeaderActions();
        });
    }

    private bool HasEditingFocus() => this.Editor.HasEditingFocus;

    private bool HasFocusWithin(DependencyObject ancestor)
    {
        if (this.RootGrid.XamlRoot is not { } root) { return false; }
        for (var element = FocusManager.GetFocusedElement(root) as DependencyObject;
             element is not null; element = VisualTreeHelper.GetParent(element))
        {
            if (ReferenceEquals(element, ancestor)) { return true; }
        }
        return false;
    }

    private void OnSwitchPaneInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (this.HasEditingFocus()) { this.NoteDetailsButton.Focus(FocusState.Keyboard); }
        else { this.Editor.FocusText(FocusState.Keyboard); }
        args.Handled = true;
    }

    private void OnHeaderPointerEntered(object sender, PointerRoutedEventArgs args)
    {
        this._isHeaderHovered = true;
        this.UpdateHeaderActions();
    }

    private void OnHeaderPointerExited(object sender, PointerRoutedEventArgs args)
    {
        this._isHeaderHovered = false;
        this.UpdateHeaderActions();
    }

    private void OnCaptionPointerEntered(InputNonClientPointerSource sender, NonClientPointerEventArgs args)
    {
        this._isCaptionHovered = args.RegionKind == NonClientRegionKind.Caption;
        this.UpdateHeaderActions();
    }

    private void OnCaptionPointerExited(InputNonClientPointerSource sender, NonClientPointerEventArgs args)
    {
        this._isCaptionHovered = false;
        this.UpdateHeaderActions();
    }

    private void OnHeaderFlyoutOpened(object sender, object args)
    {
        this._isHeaderFlyoutOpen = true;
        this.UpdateHeaderActions();
    }

    private void OnHeaderFlyoutClosed(object sender, object args)
    {
        this._isHeaderFlyoutOpen = false;
        this.QueueChromeRefresh();
    }

    private void UpdateHeaderActions()
    {
        if (this._isClosed || this.HeaderActions is null) { return; }
        var show = App.Current.IsHighContrast || this._isHeaderHovered || this._isCaptionHovered
            || this._isHeaderFlyoutOpen
            || (this._isWindowActive && this.RootGrid.XamlRoot is { } root
                && FocusManager.GetFocusedElement(root) is Control { FocusState: FocusState.Keyboard }
                && this.HasFocusWithin(this.HeaderActions));
        // Opacity keeps the header's layout, pointer targets, and keyboard tab stops
        // stable. Focus reveals the controls before a keyboard user operates them.
        this.HeaderActions.Opacity = show ? 1 : 0;
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs args)
    {
        if (this.AppWindow.Presenter is OverlappedPresenter presenter) { presenter.Minimize(); }
    }

    // Window.Close destroys the window; custom buttons must run the same save guard
    // as system close requests before calling it.
    private async void OnCloseClick(object sender, RoutedEventArgs args) => await this.CloseAfterSavingAsync();

    private void UpdateDetailsToolTip()
    {
        var details = $"Updated {this._note.UpdatedAt.ToLocalTime():g}\n{this.SaveStatusText.Text}";
        ToolTipService.SetToolTip(this.NoteDetailsButton, details);
        AutomationProperties.SetHelpText(this.NoteDetailsButton, details);
    }
}
