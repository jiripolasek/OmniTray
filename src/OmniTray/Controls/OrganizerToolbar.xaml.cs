// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Numerics;
using Windows.UI.ViewManagement;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using OmniTray.ViewModels.Organizer;

namespace OmniTray.Controls;

public sealed partial class OrganizerToolbar : UserControl
{
    private const float SelectionSurfaceElevation = 8;

    public static readonly DependencyProperty IsSelectionActiveProperty = DependencyProperty.Register(
        nameof(IsSelectionActive),
        typeof(bool),
        typeof(OrganizerToolbar),
        new PropertyMetadata(false, OnIsSelectionActiveChanged));

    public static readonly DependencyProperty CollectionViewModeProperty = DependencyProperty.Register(
        nameof(CollectionViewMode),
        typeof(OrganizerCollectionViewMode),
        typeof(OrganizerToolbar),
        new PropertyMetadata(OrganizerCollectionViewMode.Medium, OnCollectionViewModeChanged));

    public static readonly DependencyProperty SelectionSummaryProperty = DependencyProperty.Register(
        nameof(SelectionSummary),
        typeof(string),
        typeof(OrganizerToolbar),
        new PropertyMetadata(string.Empty));

    private readonly bool _animationsEnabled;
    private readonly ObservableCollection<ICommandBarElement> _permanentCommands = [];
    private readonly ObservableCollection<ICommandBarElement> _selectionCommands = [];
    private readonly ObservableCollection<ICommandBarElement> _permanentSecondaryCommands = [];
    private readonly ObservableCollection<ICommandBarElement> _selectionSecondaryCommands = [];
    private readonly ObservableCollection<ICommandBarElement> _viewCommands = [];
    private readonly AppBarSeparator _selectionCommandsSeparator = new();
    private readonly Dictionary<UIElement, long> _actionVisibilityCallbacks = [];
    private Storyboard? _selectionBackgroundTransition;
    private int _selectionBackgroundTransitionVersion;

    public event EventHandler? ClearSelectionRequested;
    public event EventHandler? CollectionViewModeChanged;
    public event EventHandler? DetailsPaneToggleRequested;

    public bool IsSelectionActive
    {
        get => (bool)this.GetValue(IsSelectionActiveProperty);
        set => this.SetValue(IsSelectionActiveProperty, value);
    }

    public OrganizerCollectionViewMode CollectionViewMode
    {
        get => (OrganizerCollectionViewMode)this.GetValue(CollectionViewModeProperty);
        set => this.SetValue(CollectionViewModeProperty, value);
    }

    public string SelectionSummary
    {
        get => (string)this.GetValue(SelectionSummaryProperty);
        set => this.SetValue(SelectionSummaryProperty, value);
    }

    public IList<ICommandBarElement> PermanentCommands => this._permanentCommands;
    public IList<ICommandBarElement> SelectionCommands => this._selectionCommands;
    public IList<ICommandBarElement> PermanentSecondaryCommands => this._permanentSecondaryCommands;
    public IList<ICommandBarElement> SelectionSecondaryCommands => this._selectionSecondaryCommands;
    public IList<ICommandBarElement> ViewCommands => this._viewCommands;

    public OrganizerToolbar()
    {
        this.InitializeComponent();
        this.SelectionBackground.Translation = new Vector3(0, 0, SelectionSurfaceElevation);
        this._animationsEnabled = new UISettings().AnimationsEnabled;
        this._permanentCommands.CollectionChanged += this.OnActionCommandsChanged;
        this._selectionCommands.CollectionChanged += this.OnActionCommandsChanged;
        this._permanentSecondaryCommands.CollectionChanged += this.OnActionCommandsChanged;
        this._selectionSecondaryCommands.CollectionChanged += this.OnActionCommandsChanged;
        this._viewCommands.CollectionChanged += this.OnViewCommandsChanged;
        this.RebuildActionCommands();
        this.UpdateCollectionViewMode(OrganizerCollectionViewMode.Medium);
    }

    public void SetDetailsPaneState(bool isVisible, bool isAvailable)
    {
        this.DetailsPaneButton.IsEnabled = isAvailable;
        this.DetailsPaneIcon.Glyph = isVisible ? "\uE89F" : "\uE8A0";
        var description = isAvailable
            ? isVisible ? "Close preview pane" : "Open preview pane"
            : "Preview pane is available in a wider window";
        AutomationProperties.SetName(this.DetailsPaneButton, description);
        ToolTipService.SetToolTip(this.DetailsPaneButton, $"{description} (Alt+P)");
    }

    private static void OnIsSelectionActiveChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((OrganizerToolbar)sender).UpdateSelectionState((bool)args.NewValue);

    private static void OnCollectionViewModeChanged(
        DependencyObject sender,
        DependencyPropertyChangedEventArgs args) =>
        ((OrganizerToolbar)sender).UpdateCollectionViewMode((OrganizerCollectionViewMode)args.NewValue);

    private void OnViewCommandsChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        this.ViewCommandsBar.PrimaryCommands.Clear();
        foreach (var command in this._viewCommands)
        {
            this.ViewCommandsBar.PrimaryCommands.Add(command);
        }

        this.ViewCommandsBar.PrimaryCommands.Add(this.CollectionViewModeButton);
        this.ViewCommandsBar.PrimaryCommands.Add(this.DetailsPaneButton);
    }

    private void OnActionCommandsChanged(object? sender, NotifyCollectionChangedEventArgs args) =>
        this.RebuildActionCommands();

    private void RebuildActionCommands()
    {
        this.UnregisterActionVisibilityCallbacks();
        this.ActionCommandsBar.PrimaryCommands.Clear();
        this.ActionCommandsBar.SecondaryCommands.Clear();

        foreach (var command in this._permanentCommands)
        {
            this.ActionCommandsBar.PrimaryCommands.Add(command);
            this.RegisterActionVisibilityCallback(command);
        }

        if (this.IsSelectionActive)
        {
            this.ActionCommandsBar.PrimaryCommands.Add(this._selectionCommandsSeparator);
            foreach (var command in this._selectionCommands)
            {
                this.ActionCommandsBar.PrimaryCommands.Add(command);
                this.RegisterActionVisibilityCallback(command);
            }
        }

        foreach (var command in this._permanentSecondaryCommands)
        {
            this.ActionCommandsBar.SecondaryCommands.Add(command);
        }

        if (this.IsSelectionActive)
        {
            foreach (var command in this._selectionSecondaryCommands)
            {
                this.ActionCommandsBar.SecondaryCommands.Add(command);
            }
        }

        this.UpdateActionSeparatorVisibility();
    }

    private void RegisterActionVisibilityCallback(ICommandBarElement command)
    {
        if (command is not UIElement element || this._actionVisibilityCallbacks.ContainsKey(element))
        {
            return;
        }

        var token = element.RegisterPropertyChangedCallback(UIElement.VisibilityProperty, this.OnActionCommandVisibilityChanged);
        this._actionVisibilityCallbacks.Add(element, token);
    }

    private void UnregisterActionVisibilityCallbacks()
    {
        foreach (var (element, token) in this._actionVisibilityCallbacks)
        {
            element.UnregisterPropertyChangedCallback(UIElement.VisibilityProperty, token);
        }

        this._actionVisibilityCallbacks.Clear();
    }

    private void OnActionCommandVisibilityChanged(DependencyObject sender, DependencyProperty property) =>
        this.UpdateActionSeparatorVisibility();

    private void UpdateActionSeparatorVisibility()
    {
        var hasPermanentCommand = this._permanentCommands.Any(IsCommandVisible);
        var hasSelectionCommand = this.IsSelectionActive && this._selectionCommands.Any(IsCommandVisible);
        this._selectionCommandsSeparator.Visibility = hasPermanentCommand && hasSelectionCommand
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private static bool IsCommandVisible(ICommandBarElement command) =>
        command is not UIElement element || element.Visibility == Visibility.Visible;

    private void UpdateCollectionViewMode(OrganizerCollectionViewMode mode)
    {
        this.ListViewModeItem.IsChecked = mode == OrganizerCollectionViewMode.List;
        this.SmallViewModeItem.IsChecked = mode == OrganizerCollectionViewMode.Small;
        this.MediumViewModeItem.IsChecked = mode == OrganizerCollectionViewMode.Medium;
        this.LargeViewModeItem.IsChecked = mode == OrganizerCollectionViewMode.Large;
        this.CollectionViewModeIcon.Glyph = mode == OrganizerCollectionViewMode.List ? "\uEA37" : "\uE8A9";
        ToolTipService.SetToolTip(this.CollectionViewModeButton, mode switch
        {
            OrganizerCollectionViewMode.List => "View: List",
            OrganizerCollectionViewMode.Small => "View: Small thumbnails",
            OrganizerCollectionViewMode.Large => "View: Large thumbnails",
            _ => "View: Medium thumbnails"
        });
    }

    private void SetCollectionViewMode(OrganizerCollectionViewMode mode)
    {
        if (this.CollectionViewMode == mode)
        {
            return;
        }

        this.CollectionViewMode = mode;
        this.CollectionViewModeChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateSelectionState(bool isActive)
    {
        this.SelectionInfoButton.Visibility = isActive ? Visibility.Visible : Visibility.Collapsed;
        this.SelectionInfoSeparator.Visibility = isActive ? Visibility.Visible : Visibility.Collapsed;
        this.RebuildActionCommands();

        var version = ++this._selectionBackgroundTransitionVersion;
        this._selectionBackgroundTransition?.Stop();
        if (!this._animationsEnabled)
        {
            this.SetSelectionBackgroundFinalState(isActive);
            return;
        }

        this.SelectionBackground.Opacity = isActive ? 0 : 1;
        this.SelectionBackgroundScale.ScaleX = isActive ? 0.985 : 1;
        this.SelectionBackgroundScale.ScaleY = isActive ? 0.88 : 1;

        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = isActive ? 180 : 110;
        var transition = new Storyboard();
        AddAnimation(
            transition,
            this.SelectionBackground,
            nameof(UIElement.Opacity),
            isActive ? 0 : 1,
            isActive ? 1 : 0,
            duration,
            easing);
        AddAnimation(
            transition,
            this.SelectionBackgroundScale,
            nameof(ScaleTransform.ScaleX),
            isActive ? 0.985 : 1,
            isActive ? 1 : 0.985,
            duration,
            easing,
            true);
        AddAnimation(
            transition,
            this.SelectionBackgroundScale,
            nameof(ScaleTransform.ScaleY),
            isActive ? 0.88 : 1,
            isActive ? 1 : 0.88,
            duration,
            easing,
            true);
        transition.Completed += (_, _) =>
        {
            if (version != this._selectionBackgroundTransitionVersion)
            {
                return;
            }

            this.SetSelectionBackgroundFinalState(isActive);
            this._selectionBackgroundTransition = null;
        };
        this._selectionBackgroundTransition = transition;
        transition.Begin();
    }

    private static void AddAnimation(
        Storyboard storyboard,
        DependencyObject target,
        string property,
        double from,
        double to,
        int durationMilliseconds,
        EasingFunctionBase easing,
        bool enableDependentAnimation = false)
    {
        var animation = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = TimeSpan.FromMilliseconds(durationMilliseconds),
            EasingFunction = easing,
            EnableDependentAnimation = enableDependentAnimation
        };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        storyboard.Children.Add(animation);
    }

    private void SetSelectionBackgroundFinalState(bool isActive)
    {
        this.SelectionBackground.Opacity = isActive ? 1 : 0;
        this.SelectionBackgroundScale.ScaleX = 1;
        this.SelectionBackgroundScale.ScaleY = 1;
    }

    private void OnClearSelectionClick(object sender, RoutedEventArgs args) =>
        this.ClearSelectionRequested?.Invoke(this, EventArgs.Empty);

    private void OnListViewModeClick(object sender, RoutedEventArgs args) =>
        this.SetCollectionViewMode(OrganizerCollectionViewMode.List);

    private void OnSmallViewModeClick(object sender, RoutedEventArgs args) =>
        this.SetCollectionViewMode(OrganizerCollectionViewMode.Small);

    private void OnMediumViewModeClick(object sender, RoutedEventArgs args) =>
        this.SetCollectionViewMode(OrganizerCollectionViewMode.Medium);

    private void OnLargeViewModeClick(object sender, RoutedEventArgs args) =>
        this.SetCollectionViewMode(OrganizerCollectionViewMode.Large);

    private void OnDetailsPaneClick(object sender, RoutedEventArgs args) =>
        this.DetailsPaneToggleRequested?.Invoke(this, EventArgs.Empty);
}
