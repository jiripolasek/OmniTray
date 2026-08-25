// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

namespace OmniTray.Controls;

internal enum InsertionPlacement
{
    Before,
    After
}

public sealed partial class InsertionAdorner : UserControl
{
    public InsertionAdorner()
    {
        this.InitializeComponent();
    }

    internal void Show(Orientation listOrientation, InsertionPlacement placement)
    {
        this.Visibility = Visibility.Visible;
        if (listOrientation == Orientation.Vertical)
        {
            this.Indicator.Width = double.NaN;
            this.Indicator.Height = 3;
            this.Indicator.Margin = new Thickness(4, 0, 4, 0);
            this.Indicator.HorizontalAlignment = HorizontalAlignment.Stretch;
            this.Indicator.VerticalAlignment = placement == InsertionPlacement.Before
                ? VerticalAlignment.Top
                : VerticalAlignment.Bottom;
        }
        else
        {
            this.Indicator.Width = 3;
            this.Indicator.Height = double.NaN;
            this.Indicator.Margin = new Thickness(0, 4, 0, 4);
            this.Indicator.HorizontalAlignment = placement == InsertionPlacement.Before
                ? HorizontalAlignment.Left
                : HorizontalAlignment.Right;
            this.Indicator.VerticalAlignment = VerticalAlignment.Stretch;
        }
    }

    internal void Hide() => this.Visibility = Visibility.Collapsed;
}
