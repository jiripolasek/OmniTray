// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Color = Windows.UI.Color;
using XamlPath = Microsoft.UI.Xaml.Shapes.Path;

namespace OmniTray.Controls;

public sealed partial class NotePreview : UserControl
{
    public static readonly DependencyProperty NoteProperty = DependencyProperty.Register(nameof(Note),
        typeof(StickyNote), typeof(NotePreview), new PropertyMetadata(null, OnChanged));
    public static readonly DependencyProperty CompactProperty = DependencyProperty.Register(nameof(Compact),
        typeof(bool), typeof(NotePreview), new PropertyMetadata(false, OnChanged));
    private readonly Grid _surface = new();
    private readonly XamlPath _paper = new() { StrokeThickness = 1 };
    private readonly XamlPath _fold = new() { StrokeThickness = 1 };
    private readonly FontIcon _glyph = new() { Glyph = "\uE70B", FontSize = 19 };
    private readonly TextBlock _text = new() { FontSize = 11, TextWrapping = TextWrapping.Wrap,
        TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 3 };

    public NotePreview()
    {
        this.Content = this._surface;
        this._surface.Children.Add(this._paper);
        this._surface.Children.Add(this._text);
        this._surface.Children.Add(this._glyph);
        this._surface.Children.Add(this._fold);
        this._glyph.Margin = new Thickness(6);
        this._surface.SizeChanged += (_, _) => this.UpdateShape();
        this.IsHitTestVisible = false;
        this.ActualThemeChanged += (_, _) => this.Refresh();
        this.Loaded += (_, _) =>
        {
            App.Current.SystemColorsChanged -= this.OnSystemColorsChanged;
            App.Current.SystemColorsChanged += this.OnSystemColorsChanged;
            this.Refresh();
        };
        this.Unloaded += (_, _) => App.Current.SystemColorsChanged -= this.OnSystemColorsChanged;
        this.Visibility = Visibility.Collapsed;
    }

    public StickyNote? Note
    {
        get => (StickyNote?)this.GetValue(NoteProperty);
        set => this.SetValue(NoteProperty, value);
    }

    public bool Compact
    {
        get => (bool)this.GetValue(CompactProperty);
        set => this.SetValue(CompactProperty, value);
    }

    private static void OnChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) => ((NotePreview)sender).Refresh();

    private void OnSystemColorsChanged(object? sender, EventArgs args) => this.Refresh();

    private void Refresh()
    {
        this.Visibility = this.Note is null ? Visibility.Collapsed : Visibility.Visible;
        if (this.Note is not { } note)
        {
            return;
        }
        var dark = this.ActualTheme == ElementTheme.Dark;
        var highContrast = App.Current.IsHighContrast;
        this._paper.Fill = highContrast
            ? (Brush)Application.Current.Resources["ApplicationPageBackgroundThemeBrush"]
            : new SolidColorBrush(NotePalette.Resolve(note.Color, dark));
        this._text.Foreground = highContrast
            ? (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"]
            : new SolidColorBrush(dark ? Colors.White : Colors.Black);
        var ink = dark ? Colors.White : Colors.Black;
        this._paper.Stroke = highContrast ? this._text.Foreground
            : new SolidColorBrush(Color.FromArgb(36, ink.R, ink.G, ink.B));
        this._fold.Fill = highContrast ? this._text.Foreground
            : new SolidColorBrush(Color.FromArgb(28, ink.R, ink.G, ink.B));
        this._fold.Stroke = this._paper.Stroke;
        this._glyph.Foreground = this._text.Foreground;
        this._glyph.Visibility = this.Compact ? Visibility.Visible : Visibility.Collapsed;
        this._text.Visibility = this.Compact ? Visibility.Collapsed : Visibility.Visible;
        this._text.Text = string.IsNullOrWhiteSpace(note.Text) ? "New note" : note.Text;
        this.UpdateShape();
    }

    private void UpdateShape()
    {
        var width = this._surface.ActualWidth;
        var height = this._surface.ActualHeight;
        if (width <= 1 || height <= 1) { return; }

        const double inset = 0.5;
        var right = width - inset;
        var bottom = height - inset;
        var corner = Math.Min(4, Math.Min(width, height) / 4);
        var fold = Math.Min(14, Math.Min(width, height) * 0.22);
        // Cut the corner out of the silhouette, then draw the folded paper inside it.
        // This leaves the view's actual background visible beyond the diagonal edge.
        this._paper.Data = ClosedShape(new Point(inset + corner, inset),
            new LineSegment { Point = new Point(right - corner, inset) },
            new QuadraticBezierSegment { Point1 = new Point(right, inset), Point2 = new Point(right, inset + corner) },
            new LineSegment { Point = new Point(right, bottom - fold) },
            new LineSegment { Point = new Point(right - fold, bottom) },
            new LineSegment { Point = new Point(inset + corner, bottom) },
            new QuadraticBezierSegment { Point1 = new Point(inset, bottom), Point2 = new Point(inset, bottom - corner) },
            new LineSegment { Point = new Point(inset, inset + corner) },
            new QuadraticBezierSegment { Point1 = new Point(inset, inset), Point2 = new Point(inset + corner, inset) });
        this._fold.Data = ClosedShape(new Point(right, bottom - fold),
            new LineSegment { Point = new Point(right - fold, bottom - fold) },
            new LineSegment { Point = new Point(right - fold, bottom) });
        // Reserve the fold's height so larger text cannot paint into the cutout.
        this._text.Margin = new Thickness(6, 6, 6, fold + 2);
    }

    private static PathGeometry ClosedShape(Point start, params PathSegment[] segments)
    {
        var figure = new PathFigure { StartPoint = start, IsClosed = true };
        foreach (var segment in segments) { figure.Segments.Add(segment); }
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }
}
