// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using Microsoft.UI.Xaml.Media;
using MediaStretch = Microsoft.UI.Xaml.Media.Stretch;

namespace OmniTray.Controls;

public sealed partial class ThumbnailImagePresenter : UserControl
{
    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source),
        typeof(ImageSource),
        typeof(ThumbnailImagePresenter),
        new PropertyMetadata(null));

    public static readonly DependencyProperty StretchProperty = DependencyProperty.Register(
        nameof(Stretch),
        typeof(MediaStretch),
        typeof(ThumbnailImagePresenter),
        new PropertyMetadata(MediaStretch.UniformToFill));

    public static readonly DependencyProperty HasVideoFilmstripProperty = DependencyProperty.Register(
        nameof(HasVideoFilmstrip),
        typeof(bool),
        typeof(ThumbnailImagePresenter),
        new PropertyMetadata(false, OnPresentationChanged));

    public static readonly DependencyProperty IsShellIconProperty = DependencyProperty.Register(
        nameof(IsShellIcon),
        typeof(bool),
        typeof(ThumbnailImagePresenter),
        new PropertyMetadata(false, OnPresentationChanged));

    public static readonly DependencyProperty ShellIconSizeProperty = DependencyProperty.Register(
        nameof(ShellIconSize),
        typeof(double),
        typeof(ThumbnailImagePresenter),
        new PropertyMetadata(32d, OnPresentationChanged));

    public ThumbnailImagePresenter()
    {
        this.InitializeComponent();
        this.UpdatePresentation();
    }

    public ImageSource? Source
    {
        get => (ImageSource?)this.GetValue(SourceProperty);
        set => this.SetValue(SourceProperty, value);
    }

    public MediaStretch Stretch
    {
        get => (MediaStretch)this.GetValue(StretchProperty);
        set => this.SetValue(StretchProperty, value);
    }

    public bool HasVideoFilmstrip
    {
        get => (bool)this.GetValue(HasVideoFilmstripProperty);
        set => this.SetValue(HasVideoFilmstripProperty, value);
    }

    public bool IsShellIcon
    {
        get => (bool)this.GetValue(IsShellIconProperty);
        set => this.SetValue(IsShellIconProperty, value);
    }

    public double ShellIconSize
    {
        get => (double)this.GetValue(ShellIconSizeProperty);
        set => this.SetValue(ShellIconSizeProperty, value);
    }

    private static void OnPresentationChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((ThumbnailImagePresenter)sender).UpdatePresentation();

    private void UpdatePresentation()
    {
        this.StandardImage.Visibility = this.HasVideoFilmstrip ? Visibility.Collapsed : Visibility.Visible;
        this.VideoThumbnail.Visibility = this.HasVideoFilmstrip ? Visibility.Visible : Visibility.Collapsed;
        this.StandardImage.Width = this.IsShellIcon ? this.ShellIconSize : double.NaN;
        this.StandardImage.Height = this.IsShellIcon ? this.ShellIconSize : double.NaN;
    }
}
