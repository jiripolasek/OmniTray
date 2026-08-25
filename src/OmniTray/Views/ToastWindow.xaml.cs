// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace OmniTray.Views;

internal sealed partial class ToastWindow : TransparentWindow
{
    private readonly DispatcherQueueTimer _dismissTimer;
    private Storyboard? _activeStoryboard;
    private int _presentationVersion;

    public ToastWindow()
    {
        this.InitializeComponent();
        this._dismissTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        this._dismissTimer.Interval = TimeSpan.FromSeconds(2.5);
        this._dismissTimer.IsRepeating = false;
        this._dismissTimer.Tick += (_, _) => this.Dismiss(this._presentationVersion);
        this.Closed += (_, _) =>
        {
            this._dismissTimer.Stop();
            this._activeStoryboard?.Stop();
        };
    }

    internal void Present(
        string message,
        InfoBarSeverity severity,
        ToastPosition position)
    {
        var version = ++this._presentationVersion;
        this._activeStoryboard?.Stop();
        this.ConfigurePlacement(position);
        this.MessageText.Text = message;
        AutomationProperties.SetItemStatus(this.MessageText, severity switch
        {
            InfoBarSeverity.Success => "Success",
            InfoBarSeverity.Warning => "Warning",
            InfoBarSeverity.Error => "Error",
            _ => "Information"
        });

        this._dismissTimer.Stop();
        this.Surface.Opacity = 0;
        this.SurfaceTransform.Y = IsTopPosition(position) ? -24 : 24;
        this.AppWindow.Show(false);
        this._activeStoryboard = this.CreateTransition(
            0,
            1,
            this.SurfaceTransform.Y,
            0,
            TimeSpan.FromMilliseconds(220));
        this._activeStoryboard.Completed += (_, _) =>
        {
            if (version == this._presentationVersion)
            {
                this.Surface.Opacity = 1;
                this.SurfaceTransform.Y = 0;
            }
        };
        this._activeStoryboard.Begin();
        this._dismissTimer.Start();
    }

    private void ConfigurePlacement(ToastPosition position)
    {
        var isTop = IsTopPosition(position);
        this.Surface.HorizontalAlignment = position == ToastPosition.TopLeft
            ? HorizontalAlignment.Left
            : HorizontalAlignment.Center;
        this.Surface.VerticalAlignment = isTop
            ? VerticalAlignment.Top
            : VerticalAlignment.Bottom;
        this.Surface.Margin = position switch
        {
            ToastPosition.TopLeft => new Thickness(16, 16, 24, 24),
            ToastPosition.TopCenter => new Thickness(24, 16, 24, 24),
            _ => new Thickness(24, 24, 24, 16)
        };
    }

    private void Dismiss(int version)
    {
        if (version != this._presentationVersion)
        {
            return;
        }

        this._dismissTimer.Stop();
        this._activeStoryboard?.Stop();
        var targetOffset = this.Surface.VerticalAlignment == VerticalAlignment.Top ? -12 : 12;
        this._activeStoryboard = this.CreateTransition(
            this.Surface.Opacity,
            0,
            this.SurfaceTransform.Y,
            targetOffset,
            TimeSpan.FromMilliseconds(180));
        this._activeStoryboard.Completed += (_, _) =>
        {
            if (version == this._presentationVersion)
            {
                this.AppWindow.Hide();
            }
        };
        this._activeStoryboard.Begin();
    }

    private Storyboard CreateTransition(
        double fromOpacity,
        double toOpacity,
        double fromOffset,
        double toOffset,
        TimeSpan duration)
    {
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        var opacityAnimation = new DoubleAnimation
        {
            From = fromOpacity, To = toOpacity, Duration = duration, EasingFunction = easing
        };
        Storyboard.SetTarget(opacityAnimation, this.Surface);
        Storyboard.SetTargetProperty(opacityAnimation, nameof(UIElement.Opacity));

        var offsetAnimation = new DoubleAnimation
        {
            From = fromOffset,
            To = toOffset,
            Duration = duration,
            EasingFunction = easing,
            EnableDependentAnimation = true
        };
        Storyboard.SetTarget(offsetAnimation, this.SurfaceTransform);
        Storyboard.SetTargetProperty(offsetAnimation, nameof(TranslateTransform.Y));

        var storyboard = new Storyboard();
        storyboard.Children.Add(opacityAnimation);
        storyboard.Children.Add(offsetAnimation);
        return storyboard;
    }

    private static bool IsTopPosition(ToastPosition position) =>
        position is ToastPosition.TopLeft or ToastPosition.TopCenter;
}
