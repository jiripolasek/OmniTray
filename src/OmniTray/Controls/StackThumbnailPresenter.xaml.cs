// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

using Microsoft.UI.Xaml.Media;

namespace OmniTray.Controls;

public sealed partial class StackThumbnailPresenter : UserControl
{
    public static readonly DependencyProperty FrameBackgroundProperty = DependencyProperty.Register(
        nameof(FrameBackground),
        typeof(Brush),
        typeof(StackThumbnailPresenter),
        new PropertyMetadata(null));

    public static readonly DependencyProperty FrameBorderBrushProperty = DependencyProperty.Register(
        nameof(FrameBorderBrush),
        typeof(Brush),
        typeof(StackThumbnailPresenter),
        new PropertyMetadata(null));

    public static readonly DependencyProperty FrameBorderThicknessProperty = DependencyProperty.Register(
        nameof(FrameBorderThickness),
        typeof(Thickness),
        typeof(StackThumbnailPresenter),
        new PropertyMetadata(default(Thickness)));

    public static readonly DependencyProperty FrameCornerRadiusProperty = DependencyProperty.Register(
        nameof(FrameCornerRadius),
        typeof(CornerRadius),
        typeof(StackThumbnailPresenter),
        new PropertyMetadata(default(CornerRadius)));

    public static readonly DependencyProperty FrameOpacityProperty = DependencyProperty.Register(
        nameof(FrameOpacity),
        typeof(double),
        typeof(StackThumbnailPresenter),
        new PropertyMetadata(1d));

    public static readonly DependencyProperty FrameCenterXProperty = DependencyProperty.Register(
        nameof(FrameCenterX),
        typeof(double),
        typeof(StackThumbnailPresenter),
        new PropertyMetadata(0d));

    public static readonly DependencyProperty FrameCenterYProperty = DependencyProperty.Register(
        nameof(FrameCenterY),
        typeof(double),
        typeof(StackThumbnailPresenter),
        new PropertyMetadata(0d));

    public static readonly DependencyProperty FrameRotationProperty = DependencyProperty.Register(
        nameof(FrameRotation),
        typeof(double),
        typeof(StackThumbnailPresenter),
        new PropertyMetadata(0d));

    public static readonly DependencyProperty FrameTranslateYProperty = DependencyProperty.Register(
        nameof(FrameTranslateY),
        typeof(double),
        typeof(StackThumbnailPresenter),
        new PropertyMetadata(0d));

    public static readonly DependencyProperty IconSizeProperty = DependencyProperty.Register(
        nameof(IconSize),
        typeof(double),
        typeof(StackThumbnailPresenter),
        new PropertyMetadata(56d));

    public static readonly DependencyProperty IconOffsetXProperty = DependencyProperty.Register(
        nameof(IconOffsetX),
        typeof(double),
        typeof(StackThumbnailPresenter),
        new PropertyMetadata(0d));

    public static readonly DependencyProperty IconOffsetYProperty = DependencyProperty.Register(
        nameof(IconOffsetY),
        typeof(double),
        typeof(StackThumbnailPresenter),
        new PropertyMetadata(0d));

    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source),
        typeof(ImageSource),
        typeof(StackThumbnailPresenter),
        new PropertyMetadata(null));

    public static readonly DependencyProperty IsShellIconProperty = DependencyProperty.Register(
        nameof(IsShellIcon),
        typeof(bool),
        typeof(StackThumbnailPresenter),
        new PropertyMetadata(false, OnPresentationChanged));

    public static readonly DependencyProperty HasVideoFilmstripProperty = DependencyProperty.Register(
        nameof(HasVideoFilmstrip),
        typeof(bool),
        typeof(StackThumbnailPresenter),
        new PropertyMetadata(false, OnPresentationChanged));

    public Brush? FrameBackground
    {
        get => (Brush?)this.GetValue(FrameBackgroundProperty);
        set => this.SetValue(FrameBackgroundProperty, value);
    }

    public Brush? FrameBorderBrush
    {
        get => (Brush?)this.GetValue(FrameBorderBrushProperty);
        set => this.SetValue(FrameBorderBrushProperty, value);
    }

    public Thickness FrameBorderThickness
    {
        get => (Thickness)this.GetValue(FrameBorderThicknessProperty);
        set => this.SetValue(FrameBorderThicknessProperty, value);
    }

    public CornerRadius FrameCornerRadius
    {
        get => (CornerRadius)this.GetValue(FrameCornerRadiusProperty);
        set => this.SetValue(FrameCornerRadiusProperty, value);
    }

    public double FrameOpacity
    {
        get => (double)this.GetValue(FrameOpacityProperty);
        set => this.SetValue(FrameOpacityProperty, value);
    }

    public double FrameCenterX
    {
        get => (double)this.GetValue(FrameCenterXProperty);
        set => this.SetValue(FrameCenterXProperty, value);
    }

    public double FrameCenterY
    {
        get => (double)this.GetValue(FrameCenterYProperty);
        set => this.SetValue(FrameCenterYProperty, value);
    }

    public double FrameRotation
    {
        get => (double)this.GetValue(FrameRotationProperty);
        set => this.SetValue(FrameRotationProperty, value);
    }

    public double FrameTranslateY
    {
        get => (double)this.GetValue(FrameTranslateYProperty);
        set => this.SetValue(FrameTranslateYProperty, value);
    }

    public double IconSize
    {
        get => (double)this.GetValue(IconSizeProperty);
        set => this.SetValue(IconSizeProperty, value);
    }

    public double IconOffsetX
    {
        get => (double)this.GetValue(IconOffsetXProperty);
        set => this.SetValue(IconOffsetXProperty, value);
    }

    public double IconOffsetY
    {
        get => (double)this.GetValue(IconOffsetYProperty);
        set => this.SetValue(IconOffsetYProperty, value);
    }

    public ImageSource? Source
    {
        get => (ImageSource?)this.GetValue(SourceProperty);
        set => this.SetValue(SourceProperty, value);
    }

    public bool IsShellIcon
    {
        get => (bool)this.GetValue(IsShellIconProperty);
        set => this.SetValue(IsShellIconProperty, value);
    }

    public bool HasVideoFilmstrip
    {
        get => (bool)this.GetValue(HasVideoFilmstripProperty);
        set => this.SetValue(HasVideoFilmstripProperty, value);
    }

    public StackThumbnailPresenter()
    {
        this.InitializeComponent();
        this.UpdatePresentation();
    }

    private static void OnPresentationChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((StackThumbnailPresenter)sender).UpdatePresentation();

    private void UpdatePresentation()
    {
        this.ThumbnailFrame.Visibility = this.IsShellIcon ? Visibility.Collapsed : Visibility.Visible;
        this.ShellIcon.Visibility = this.IsShellIcon ? Visibility.Visible : Visibility.Collapsed;
    }
}
