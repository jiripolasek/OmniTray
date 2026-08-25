// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

using Microsoft.UI;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;

namespace OmniTray.Controls;

public sealed partial class StackColorPicker : UserControl
{
    private const int PaletteColumnCount = 9;
    private readonly List<(StackTintPreset Preset, Border Selection)> _presetSelections = [];
    private Action<string>? _changeTint;
    private Func<string>? _getTint;
    private DropStackViewModel? _stack;

    public StackColorPicker()
    {
        this.InitializeComponent();
        this.BuildPalette();
        this.NeutralSwatch.Background = new SolidColorBrush(
            StackTintPalette.Resolve(DropStack.DefaultTint));
        this.SystemAccentSwatch.Background = new SolidColorBrush(
            StackTintPalette.Resolve(DropStack.SystemAccentTint));
    }

    public DropStackViewModel? Stack
    {
        get => this._stack;
        set
        {
            this._stack = value;
            this.UpdateSelection();
        }
    }

    public event EventHandler? ColorSelected;

    internal void Configure(Func<string> getTint, Action<string> changeTint)
    {
        this._getTint = getTint ?? throw new ArgumentNullException(nameof(getTint));
        this._changeTint = changeTint ?? throw new ArgumentNullException(nameof(changeTint));
        this._stack = null;
        this.UpdateSelection();
    }

    internal void PrepareForDisplay()
    {
        this.NeutralSwatch.Background = new SolidColorBrush(
            StackTintPalette.Resolve(DropStack.DefaultTint));
        this.SystemAccentSwatch.Background = new SolidColorBrush(
            StackTintPalette.Resolve(DropStack.SystemAccentTint));
        this.UpdateSelection();
        _ = this.NeutralButton.DispatcherQueue.TryEnqueue(() => this.NeutralButton.Focus(FocusState.Programmatic));
    }

    private void BuildPalette()
    {
        for (var column = 0; column < PaletteColumnCount; column++)
        {
            this.PaletteGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        }

        var rowCount = (StackTintPalette.Presets.Count + PaletteColumnCount - 1) / PaletteColumnCount;
        for (var row = 0; row < rowCount; row++)
        {
            this.PaletteGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        for (var index = 0; index < StackTintPalette.Presets.Count; index++)
        {
            var preset = StackTintPalette.Presets[index];
            var selection = CreateSelectionIndicator();
            var swatch = new Grid
            {
                Width = 28,
                Height = 28,
                Children =
                {
                    new Border
                    {
                        Background = new SolidColorBrush(StackTintPalette.Resolve(preset.Tint)),
                        BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(80, 255, 255, 255)),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(4)
                    },
                    selection
                }
            };
            var button = new Button
            {
                Width = 30,
                Height = 30,
                Padding = new Thickness(1),
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(6),
                Content = swatch,
                Tag = preset
            };
            AutomationProperties.SetName(button, preset.Name);
            ToolTipService.SetToolTip(button, preset.Name);
            button.Click += this.OnPresetClick;
            Grid.SetColumn(button, index % PaletteColumnCount);
            Grid.SetRow(button, index / PaletteColumnCount);
            this.PaletteGrid.Children.Add(button);
            this._presetSelections.Add((preset, selection));
        }
    }

    private static Border CreateSelectionIndicator() => new()
    {
        Width = 16,
        Height = 16,
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Top,
        Background = new SolidColorBrush(Colors.White),
        BorderBrush = new SolidColorBrush(Colors.Black),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(3),
        Child = new FontIcon { FontSize = 10, Foreground = new SolidColorBrush(Colors.Black), Glyph = "\uE73E" },
        Visibility = Visibility.Collapsed
    };

    private void OnNeutralClick(object sender, RoutedEventArgs args)
    {
        this.SelectTint(DropStack.DefaultTint);
    }

    private void OnSystemAccentClick(object sender, RoutedEventArgs args)
    {
        this.SelectTint(DropStack.SystemAccentTint);
    }

    private void OnPresetClick(object sender, RoutedEventArgs args)
    {
        if ((sender as FrameworkElement)?.Tag is not StackTintPreset preset)
        {
            return;
        }

        this.SelectTint(preset.Tint);
    }

    private void UpdateSelection()
    {
        var tint = this.Stack?.Tint ?? this._getTint?.Invoke();
        var usesNeutral = tint is not null && StackTintPalette.IsNeutral(tint);
        var usesSystemAccent = tint is not null && StackTintPalette.IsSystemAccent(tint);
        this.NeutralButton.IsChecked = usesNeutral;
        this.NeutralSelection.Visibility = usesNeutral
            ? Visibility.Visible
            : Visibility.Collapsed;
        this.SystemAccentButton.IsChecked = usesSystemAccent;
        this.SystemAccentSelection.Visibility = usesSystemAccent
            ? Visibility.Visible
            : Visibility.Collapsed;

        foreach (var (preset, selection) in this._presetSelections)
        {
            var isSelected = string.Equals(tint, preset.Tint, StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(tint, preset.Name, StringComparison.OrdinalIgnoreCase);
            selection.Visibility = isSelected ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void SelectTint(string tint)
    {
        if (this.Stack is { } stack)
        {
            stack.ChangeTint(tint);
        }
        else if (this._changeTint is { } changeTint)
        {
            changeTint(tint);
        }
        else
        {
            return;
        }

        this.UpdateSelection();
        this.ColorSelected?.Invoke(this, EventArgs.Empty);
    }
}

internal static class TrayColorPaletteFlyout
{
    public static Flyout Create(Func<string> getTint, Action<string> changeTint)
    {
        ArgumentNullException.ThrowIfNull(getTint);
        ArgumentNullException.ThrowIfNull(changeTint);
        var picker = new StackColorPicker();
        picker.Configure(getTint, changeTint);
        var flyout = new Flyout { Content = picker };
        picker.ColorSelected += (_, _) => flyout.Hide();
        return flyout;
    }

    public static void Show(
        FrameworkElement target,
        Func<string> getTint,
        Action<string> changeTint)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.XamlRoot is null)
        {
            return;
        }

        var flyout = Create(getTint, changeTint);
        flyout.ShowAt(target);
    }
}
