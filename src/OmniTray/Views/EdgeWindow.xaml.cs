// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Numerics;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI.ViewManagement;
using Microsoft.UI.Composition;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using OmniTray.Controls;

namespace OmniTray.Views;

public sealed partial class EdgeWindow : TransparentWindow
{
    internal const double HintThickness = 48;
    private const float ShadowElevation = 32;
    private static readonly TimeSpan RevealAnimationDuration = TimeSpan.FromMilliseconds(220);
    private readonly bool _animationsEnabled;

    private readonly Compositor _compositor;
    private readonly InsetClip _contentClip;
    private readonly ListInsertionAdornerController _horizontalStackInsertionAdorner;
    private readonly PointerEventHandler _stackPointerMovedHandler;
    private readonly ListInsertionAdornerController _verticalStackInsertionAdorner;
    private ListView _activeStackList = null!;
    private ScalarKeyFrameAnimation? _contentClipAnimation;
    private double _edgeInset;
    private FrameworkElement? _expandedVerticalOrganizer;
    private ScalarKeyFrameAnimation? _hintRailAnimation;
    private DropStackViewModel? _horizontalExpandedStack;
    private bool _isExpandedTarget;
    private bool _isStackDragOperationActive;
    private double _panelHeight;
    private double _panelWidth;
    private Vector3KeyFrameAnimation? _revealAnimation;
    private int _revealGeneration;
    private ScrollViewer? _stackScrollViewer;

    public EdgeWindow(MainViewModel viewModel, EdgeShelfSide side)
    {
        this.ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        this.Side = side;
        this.EdgeStacks = this.ViewModel.GetEdgeStacks(side);
        this.ShelfTitle = $"{side.GetDisplayName()} edge shelf";
        this._stackPointerMovedHandler = this.OnStackPointerMoved;
        this.InitializeComponent();
        var commandSurfaceId = DropCommandSurfaceIds.ForEdge(side);
        this.VerticalCommandSurface.SurfaceId = commandSurfaceId;
        this.VerticalCommandSurface.OwnerWindow = this;
        this.HorizontalCommandSurface.SurfaceId = commandSurfaceId;
        this.HorizontalCommandSurface.OwnerWindow = this;
        this.VerticalCommandSurface.ExternalDragEntered += this.OnCommandSurfaceExternalDragEntered;
        this.HorizontalCommandSurface.ExternalDragEntered += this.OnCommandSurfaceExternalDragEntered;
        this.VerticalCommandSurface.ExternalDragLeft += this.OnCommandSurfaceExternalDragLeft;
        this.HorizontalCommandSurface.ExternalDragLeft += this.OnCommandSurfaceExternalDragLeft;
        this.VerticalCommandSurface.CommandDropCompleted += this.OnCommandSurfaceDropCompleted;
        this.HorizontalCommandSurface.CommandDropCompleted += this.OnCommandSurfaceDropCompleted;
        // Edge hosts are first shown without activation so they do not steal focus.
        // Initialize root x:Bind expressions now instead of waiting for Activated.
        this.Bindings.Update();

        this._compositor = CompositionTarget.GetCompositorForCurrentThread();
        this._contentClip = this._compositor.CreateInsetClip();
        ElementCompositionPreview.GetElementVisual(this.ContentRoot).Clip = this._contentClip;
        this._animationsEnabled = new UISettings().AnimationsEnabled;
        this.ShelfBackdrop.Translation = new Vector3(0, 0, ShadowElevation);
        this._verticalStackInsertionAdorner = new ListInsertionAdornerController(this.EdgeStackList,
            "StackInsertionAdorner",
            Orientation.Vertical);
        this._horizontalStackInsertionAdorner = new ListInsertionAdornerController(this.HorizontalStackList,
            "StackInsertionAdorner",
            Orientation.Horizontal);
        this.ConfigureOrientation();
        this.EdgeStacks.CollectionChanged += this.OnEdgeStacksChanged;
        this.EdgeStackList.Loaded += (_, _) => this.CacheActiveStackScrollViewer(this.EdgeStackList);
        this.HorizontalStackList.Loaded += (_, _) => this.CacheActiveStackScrollViewer(this.HorizontalStackList);
        this.UpdateEmptyState();
    }

    public MainViewModel ViewModel { get; }

    public EdgeShelfSide Side { get; }

    public ObservableCollection<DropStackViewModel> EdgeStacks { get; }

    public string ShelfTitle { get; }

    internal bool IsHorizontalDetailExpanded => this._horizontalExpandedStack is not null;

    public event EventHandler? CollapseRequested;

    public event EventHandler? PointerInteractionStarted;

    public event EventHandler? PointerInteractionEnded;

    public event EventHandler? ExternalDragEntered;

    public event EventHandler? ExternalDragLeft;

    public event EventHandler? DropCompleted;

    internal event EventHandler? HorizontalDetailExpansionChanged;

    internal void ResetCommandNavigation()
    {
        this.VerticalCommandSurface.ResetNavigation();
        this.HorizontalCommandSurface.ResetNavigation();
    }

    internal void ConfigurePanelSize(
        double width,
        double height,
        double offsetX,
        double offsetY,
        double edgeInset)
    {
        this._panelWidth = width;
        this._panelHeight = height;
        this._edgeInset = edgeInset;
        this.ShelfSurface.Width = width;
        this.ShelfSurface.Height = height;
        this.ShelfSurface.HorizontalAlignment = HorizontalAlignment.Left;
        this.ShelfSurface.VerticalAlignment = VerticalAlignment.Top;
        this.ShelfSurface.Margin = new Thickness(offsetX, offsetY, 0, 0);
        this.SetRevealState(this._isExpandedTarget, false);
    }

    internal void SetRevealState(bool expanded, bool animate, Action? completed = null)
    {
        this._isExpandedTarget = expanded;
        var target = this.GetRevealTranslation(expanded);
        var targetHintOpacity = expanded ? 0f : 1f;
        var targetContentInset = expanded ? 0f : (float)HintThickness;
        var contentClipProperty = this.GetContentClipProperty();
        var generation = ++this._revealGeneration;
        if (!animate || !this._animationsEnabled)
        {
            this.StopRevealAnimations();
            this.ShelfSurface.Translation = target;
            this.SetContentClipInset(targetContentInset);
            this.SetHintRailRestingState(expanded);

            completed?.Invoke();
            return;
        }

        this.HintRail.Visibility = Visibility.Visible;
        var easing = this._compositor.CreateCubicBezierEasingFunction(
            new Vector2(0.2f, 0),
            new Vector2(0, 1));
        var animation = this._compositor.CreateVector3KeyFrameAnimation();
        animation.Target = nameof(UIElement.Translation);
        animation.Duration = RevealAnimationDuration;
        animation.StopBehavior = AnimationStopBehavior.SetToFinalValue;
        animation.InsertExpressionKeyFrame(0, "this.StartingValue");
        animation.InsertKeyFrame(1, target, easing);

        var hintRailAnimation = this._compositor.CreateScalarKeyFrameAnimation();
        hintRailAnimation.Target = nameof(UIElement.Opacity);
        hintRailAnimation.Duration = RevealAnimationDuration;
        hintRailAnimation.StopBehavior = AnimationStopBehavior.SetToFinalValue;
        hintRailAnimation.InsertExpressionKeyFrame(0, "this.StartingValue");
        hintRailAnimation.InsertKeyFrame(1, targetHintOpacity, easing);

        var contentClipAnimation = this._compositor.CreateScalarKeyFrameAnimation();
        contentClipAnimation.Duration = RevealAnimationDuration;
        contentClipAnimation.StopBehavior = AnimationStopBehavior.SetToFinalValue;
        contentClipAnimation.InsertExpressionKeyFrame(0, "this.StartingValue");
        contentClipAnimation.InsertKeyFrame(1, targetContentInset, easing);

        var batch = this._compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        batch.Completed += (_, _) =>
        {
            if (generation != this._revealGeneration)
            {
                return;
            }

            this.ShelfSurface.Translation = target;
            this.HintRail.Opacity = targetHintOpacity;
            this.SetContentClipInset(targetContentInset);
            this.ShelfSurface.StopAnimation(animation);
            this.HintRail.StopAnimation(hintRailAnimation);
            this._contentClip.StopAnimation(contentClipProperty);
            this.HintRail.Visibility = expanded ? Visibility.Collapsed : Visibility.Visible;
            this._revealAnimation = null;
            this._hintRailAnimation = null;
            this._contentClipAnimation = null;
            completed?.Invoke();
        };

        this._revealAnimation = animation;
        this._hintRailAnimation = hintRailAnimation;
        this._contentClipAnimation = contentClipAnimation;
        this.ShelfSurface.StartAnimation(animation);
        this.HintRail.StartAnimation(hintRailAnimation);
        this._contentClip.StartAnimation(contentClipProperty, contentClipAnimation);
        batch.End();
    }

    public void Detach()
    {
        this.EdgeStacks.CollectionChanged -= this.OnEdgeStacksChanged;
        if (this._horizontalExpandedStack is { } expandedStack)
        {
            expandedStack.ModelChanged -= this.OnHorizontalExpandedStackChanged;
        }

        this._revealGeneration++;
        this.StopRevealAnimations();
    }

    private void StopRevealAnimations()
    {
        if (this._revealAnimation is { } runningAnimation)
        {
            this.ShelfSurface.StopAnimation(runningAnimation);
            this._revealAnimation = null;
        }

        if (this._hintRailAnimation is { } runningHintRailAnimation)
        {
            this.HintRail.StopAnimation(runningHintRailAnimation);
            this._hintRailAnimation = null;
        }

        if (this._contentClipAnimation is not null)
        {
            this._contentClip.StopAnimation(this.GetContentClipProperty());
            this._contentClipAnimation = null;
        }
    }

    private void SetHintRailRestingState(bool expanded)
    {
        this.HintRail.Opacity = expanded ? 0 : 1;
        this.HintRail.Visibility = expanded ? Visibility.Collapsed : Visibility.Visible;
    }

    private string GetContentClipProperty() =>
        this.Side switch
        {
            EdgeShelfSide.Left => nameof(InsetClip.RightInset),
            EdgeShelfSide.Right => nameof(InsetClip.LeftInset),
            EdgeShelfSide.Top => nameof(InsetClip.BottomInset),
            EdgeShelfSide.Bottom => nameof(InsetClip.TopInset),
            _ => throw new ArgumentOutOfRangeException(nameof(this.Side))
        };

    private void SetContentClipInset(float inset)
    {
        this._contentClip.LeftInset = this.Side == EdgeShelfSide.Right ? inset : 0;
        this._contentClip.RightInset = this.Side == EdgeShelfSide.Left ? inset : 0;
        this._contentClip.TopInset = this.Side == EdgeShelfSide.Bottom ? inset : 0;
        this._contentClip.BottomInset = this.Side == EdgeShelfSide.Top ? inset : 0;
    }

    private Vector3 GetRevealTranslation(bool expanded)
    {
        if (expanded)
        {
            return Vector3.Zero;
        }

        if (this.Side.IsVertical())
        {
            var travel = (float)Math.Max(0, this._panelWidth - HintThickness + this._edgeInset);
            return new Vector3(this.Side == EdgeShelfSide.Left ? -travel : travel, 0, 0);
        }

        var verticalTravel = (float)Math.Max(0, this._panelHeight - HintThickness + this._edgeInset);
        return new Vector3(0, this.Side == EdgeShelfSide.Top ? -verticalTravel : verticalTravel, 0);
    }

    private void ConfigureOrientation()
    {
        var vertical = this.Side.IsVertical();
        this.VerticalShelfLayout.Visibility = vertical ? Visibility.Visible : Visibility.Collapsed;
        this.HorizontalShelfLayout.Visibility = vertical ? Visibility.Collapsed : Visibility.Visible;
        this._activeStackList = vertical ? this.EdgeStackList : this.HorizontalStackList;
        this._stackScrollViewer = null;

        if (!vertical)
        {
            var targetRow = this.Side == EdgeShelfSide.Top ? 0 : 1;
            var detailRow = targetRow == 0 ? 1 : 0;
            Grid.SetRow(this.HorizontalTargetRail, targetRow);
            Grid.SetRow(this.HorizontalDetailPanel, detailRow);
            this.HorizontalDetailPanel.BorderThickness = this.Side == EdgeShelfSide.Top
                ? new Thickness(0, 1, 0, 0)
                : new Thickness(0, 0, 0, 1);
            this.HorizontalDetailCollapseIcon.Glyph = this.Side == EdgeShelfSide.Top
                ? "\uE70E"
                : "\uE70D";
            this.HorizontalShelfLayout.RowDefinitions[0].Height = targetRow == 0
                ? GridLength.Auto
                : new GridLength(1, GridUnitType.Star);
            this.HorizontalShelfLayout.RowDefinitions[1].Height = targetRow == 1
                ? GridLength.Auto
                : new GridLength(1, GridUnitType.Star);
        }

        this.HintRail.Width = vertical ? HintThickness : double.NaN;
        this.HintRail.Height = vertical ? double.NaN : HintThickness;
        this.HintRail.HorizontalAlignment = this.Side switch
        {
            EdgeShelfSide.Left => HorizontalAlignment.Right,
            EdgeShelfSide.Right => HorizontalAlignment.Left,
            _ => HorizontalAlignment.Stretch
        };
        this.HintRail.VerticalAlignment = this.Side switch
        {
            EdgeShelfSide.Top => VerticalAlignment.Bottom,
            EdgeShelfSide.Bottom => VerticalAlignment.Top,
            _ => VerticalAlignment.Stretch
        };
        this.HintRailIcon.Glyph = this.Side switch
        {
            EdgeShelfSide.Left => "\uE76C",
            EdgeShelfSide.Right => "\uE76B",
            EdgeShelfSide.Top => "\uE70D",
            EdgeShelfSide.Bottom => "\uE70E",
            _ => string.Empty
        };
        if (vertical)
        {
            ConfigureScrollZone(this.LeadingScrollZone, HorizontalAlignment.Stretch, VerticalAlignment.Top, double.NaN,
                38);
            ConfigureScrollZone(this.TrailingScrollZone, HorizontalAlignment.Stretch, VerticalAlignment.Bottom,
                double.NaN, 38);
            this.LeadingScrollIcon.Glyph = "\uE70E";
            this.TrailingScrollIcon.Glyph = "\uE70D";
        }
        else
        {
            var railAlignment = this.Side == EdgeShelfSide.Top
                ? VerticalAlignment.Top
                : VerticalAlignment.Bottom;
            ConfigureScrollZone(this.LeadingScrollZone, HorizontalAlignment.Left, railAlignment, 38, 122);
            ConfigureScrollZone(this.TrailingScrollZone, HorizontalAlignment.Right, railAlignment, 38, 122);
            this.LeadingScrollIcon.Glyph = "\uE76B";
            this.TrailingScrollIcon.Glyph = "\uE76C";
        }
    }

    private void CacheActiveStackScrollViewer(ListView list)
    {
        if (ReferenceEquals(list, this._activeStackList))
        {
            this._stackScrollViewer = this.Side.IsVertical()
                ? FindDescendant<ScrollViewer>(list)
                : this.HorizontalRailScrollViewer;
        }
    }

    private static void ConfigureScrollZone(
        FrameworkElement element,
        HorizontalAlignment horizontalAlignment,
        VerticalAlignment verticalAlignment,
        double width,
        double height)
    {
        element.HorizontalAlignment = horizontalAlignment;
        element.VerticalAlignment = verticalAlignment;
        element.Width = width;
        element.Height = height;
    }

    private void OnRootDragEnter(object sender, DragEventArgs args)
    {
        this.ShowDragScrollZones();
        this.ExternalDragEntered?.Invoke(this, EventArgs.Empty);
    }

    private void OnRootDragOver(object sender, DragEventArgs args)
    {
        this.ExternalDragEntered?.Invoke(this, EventArgs.Empty);
        if (DragDropDataService.HasItemReference(args.DataView))
        {
            args.Handled = true;
            args.AcceptedOperation = DataPackageOperation.None;
            args.DragUIOverride.Caption = "Drop onto a stack or New stack";
            args.DragUIOverride.IsCaptionVisible = true;
            args.DragUIOverride.IsContentVisible = true;
            return;
        }

        if (DragDropDataService.HasStackReference(args.DataView))
        {
            args.AcceptedOperation = DragDropDataService.GetAcceptedInternalMoveOperation(args.DataView);
            args.DragUIOverride.Caption = $"Move stack to {this.Side.GetDisplayName().ToLowerInvariant()} edge";
            return;
        }

        ConfigureContentDragOver(args, $"Create a stack on the {this.Side.GetDisplayName().ToLowerInvariant()} edge");
    }

    private void OnRootDragLeave(object sender, DragEventArgs args)
    {
        this.HideDragScrollZones();
        this.ExternalDragLeft?.Invoke(this, EventArgs.Empty);
    }

    private async void OnRootDrop(object sender, DragEventArgs args)
    {
        this.HideDragScrollZones();
        if (DragDropDataService.HasItemReference(args.DataView))
        {
            args.Handled = true;
            this.ExternalDragLeft?.Invoke(this, EventArgs.Empty);
            this.DropCompleted?.Invoke(this, EventArgs.Empty);
            return;
        }

        await this.AddDropToEdgeAsync(args.DataView, IsCopyRequested(args));
        this.DropCompleted?.Invoke(this, EventArgs.Empty);
    }

    private void OnStackDragOver(object sender, DragEventArgs args)
    {
        this.ExternalDragEntered?.Invoke(this, EventArgs.Empty);
        if (DragDropDataService.HasItemReference(args.DataView))
        {
            args.Handled = true;
            var target = GetTaggedStack(sender);
            var isSameStack = target is not null &&
                              DragDropDataService.ActiveItemReference?.SourceStackId == target.Model.Id;
            SetStackDropOutline(sender, target is not null && !isSameStack);
            if (target is not null)
            {
                ConfigureItemTransferDragOver(args, $"Add to {target.Name}", target);
            }

            return;
        }

        if (DragDropDataService.HasStackReference(args.DataView))
        {
            SetStackDropOutline(sender, false);
            return;
        }

        args.Handled = true;
        var stack = GetTaggedStack(sender);
        SetStackDropOutline(
            sender,
            stack is not null && ConfigureContentDragOver(args, $"Add to {stack.Name}"));
    }

    private void OnStackDragLeave(object sender, DragEventArgs args) =>
        SetStackDropOutline(sender, false);

    private async void OnStackDrop(object sender, DragEventArgs args)
    {
        SetStackDropOutline(sender, false);
        if (DragDropDataService.HasStackReference(args.DataView))
        {
            return;
        }

        args.Handled = true;
        var stack = GetTaggedStack(sender);
        if (stack is null || !this.EdgeStacks.Contains(stack))
        {
            return;
        }

        if (DragDropDataService.HasItemReference(args.DataView))
        {
            if (DragDropDataService.ActiveItemReference?.SourceStackId == stack.Model.Id)
            {
                return;
            }

            await this.TransferItemsIntoStackAsync(
                args.DataView,
                stack,
                stack.Items.Count,
                IsCopyRequested(args));
            this.HideDragScrollZones();
            this.DropCompleted?.Invoke(this, EventArgs.Empty);
            return;
        }

        try
        {
            var items = await DragDropDataService.ReadAsync(args.DataView);
            if (items.Count == 0)
            {
                ShowStatus("This drag did not contain a supported payload.", InfoBarSeverity.Warning);
                return;
            }

            var addedCount = stack.AppendDroppedItems(items);
            ShowDropImportStatus(stack.Name, items.Count, addedCount);
        }
        catch (Exception exception)
        {
            ShowStatus($"The drop could not be captured: {exception.Message}", InfoBarSeverity.Error);
        }
        finally
        {
            this.HideDragScrollZones();
            this.DropCompleted?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnNewStackDragOver(object sender, DragEventArgs args)
    {
        args.Handled = true;
        this.ExternalDragEntered?.Invoke(this, EventArgs.Empty);
        if (DragDropDataService.HasStackReference(args.DataView))
        {
            args.AcceptedOperation = DragDropDataService.GetAcceptedInternalMoveOperation(args.DataView);
            args.DragUIOverride.Caption = $"Move stack to {this.Side.GetDisplayName().ToLowerInvariant()} edge";
            return;
        }

        if (DragDropDataService.HasItemReference(args.DataView))
        {
            ConfigureItemTransferDragOver(
                args,
                $"Create a stack on the {this.Side.GetDisplayName().ToLowerInvariant()} edge");
            return;
        }

        ConfigureContentDragOver(args, $"Create a stack on the {this.Side.GetDisplayName().ToLowerInvariant()} edge");
    }

    private void OnNewStackDragLeave(object sender, DragEventArgs args) =>
        this.ExternalDragLeft?.Invoke(this, EventArgs.Empty);

    private async void OnNewStackDrop(object sender, DragEventArgs args)
    {
        args.Handled = true;
        await this.AddDropToEdgeAsync(args.DataView, IsCopyRequested(args));
        this.HideDragScrollZones();
        this.DropCompleted?.Invoke(this, EventArgs.Empty);
    }

    private void OnNewStackClick(object sender, RoutedEventArgs args)
    {
        var stack = this.ViewModel.AddStack(DropStack.CreateEmpty());
        this.ViewModel.AssignStackToEdge(stack, this.Side);
        ShowStatus($"Created an empty stack on the {this.Side.GetDisplayName().ToLowerInvariant()} edge.",
            InfoBarSeverity.Success);
    }

    private void OnStackDragStarting(UIElement sender, DragStartingEventArgs args)
    {
        var stack = GetTaggedStack(sender);
        if (stack is null)
        {
            args.Cancel = true;
            return;
        }

        DragDropDataService.Write(
            args.Data,
            stack.Model,
            stack.Name,
            App.Current.AllowMoveOnDragOutPreference);
    }

    private void OnStackDragSurfaceLoaded(object sender, RoutedEventArgs args)
    {
        if (sender is UIElement source)
        {
            source.AddHandler(
                UIElement.PointerMovedEvent, this._stackPointerMovedHandler,
                true);
        }
    }

    private void OnStackDragSurfaceUnloaded(object sender, RoutedEventArgs args)
    {
        if (sender is UIElement source)
        {
            source.RemoveHandler(UIElement.PointerMovedEvent, this._stackPointerMovedHandler);
        }
    }

    private async void OnStackPointerMoved(object sender, PointerRoutedEventArgs args)
    {
        if (this._isStackDragOperationActive ||
            !args.Pointer.IsInContact ||
            sender is not UIElement source ||
            GetTaggedStack(sender) is not { } stack)
        {
            return;
        }

        var pointerPoint = args.GetCurrentPoint(source);
        if (args.Pointer.PointerDeviceType == PointerDeviceType.Mouse &&
            !pointerPoint.Properties.IsLeftButtonPressed)
        {
            return;
        }

        this._isStackDragOperationActive = true;
        args.Handled = true;
        var dropResult = DataPackageOperation.None;
        try
        {
            dropResult = await source.StartDragAsync(pointerPoint);
        }
        catch (Exception exception)
        {
            ShowStatus($"Could not start dragging {stack.Name}: {exception.Message}", InfoBarSeverity.Error);
        }
        finally
        {
            this.ClearStackInsertionAdorners();
            await App.Current.CompleteStackDragAsync(dropResult);
            this._isStackDragOperationActive = false;
        }
    }

    private void OnStackListDragOver(object sender, DragEventArgs args)
    {
        if (sender is not ListView list || !DragDropDataService.HasStackReference(args.DataView))
        {
            return;
        }

        args.Handled = true;
        this.ExternalDragEntered?.Invoke(this, EventArgs.Empty);
        var controller = this.GetStackInsertionAdorner(list);
        var target = controller.Resolve(args.GetPosition(list));
        var source = DragDropDataService.ActiveStackReferenceId is { } stackId
            ? this.ViewModel.Stacks.FirstOrDefault(stack => stack.Model.Id == stackId)
            : null;
        var canMove = target is not null &&
                      (source is null ||
                       this.ViewModel.CanMoveStackToEdge(source, this.Side, target.Value.InsertionIndex));
        if (!canMove)
        {
            controller.Clear();
            args.AcceptedOperation = DataPackageOperation.None;
            args.DragUIOverride.Caption = "Stack is already in this position";
        }
        else
        {
            controller.Show(target!.Value);
            args.AcceptedOperation = DragDropDataService.GetAcceptedInternalMoveOperation(args.DataView);
            args.DragUIOverride.Caption
                = $"Move stack here on the {this.Side.GetDisplayName().ToLowerInvariant()} edge";
        }

        args.DragUIOverride.IsCaptionVisible = true;
        args.DragUIOverride.IsContentVisible = true;
    }

    private void OnStackListDragLeave(object sender, DragEventArgs args)
    {
        if (sender is ListView list && DragDropDataService.HasStackReference(args.DataView))
        {
            this.GetStackInsertionAdorner(list).Clear();
        }
    }

    private async void OnStackListDrop(object sender, DragEventArgs args)
    {
        if (sender is not ListView list || !DragDropDataService.HasStackReference(args.DataView))
        {
            return;
        }

        args.Handled = true;
        var controller = this.GetStackInsertionAdorner(list);
        var target = controller.Resolve(args.GetPosition(list));
        controller.Clear();
        if (target is null)
        {
            return;
        }

        var stackId = await DragDropDataService.ReadStackReferenceAsync(args.DataView);
        var stack = stackId is { } id
            ? this.ViewModel.Stacks.FirstOrDefault(candidate => candidate.Model.Id == id)
            : null;
        if (stack is null)
        {
            ShowStatus("That stack is no longer available.", InfoBarSeverity.Warning);
        }
        else
        {
            this.ViewModel.MoveStackToEdge(stack, this.Side, target.Value.InsertionIndex);
        }

        this.HideDragScrollZones();
        this.DropCompleted?.Invoke(this, EventArgs.Empty);
    }

    private ListInsertionAdornerController GetStackInsertionAdorner(ListView list) =>
        ReferenceEquals(list, this.HorizontalStackList)
            ? this._horizontalStackInsertionAdorner
            : this._verticalStackInsertionAdorner;

    private void ClearStackInsertionAdorners()
    {
        this._verticalStackInsertionAdorner.Clear();
        this._horizontalStackInsertionAdorner.Clear();
    }

    private void OnHorizontalStackItemClick(object sender, ItemClickEventArgs args)
    {
        if (args.ClickedItem is DropStackViewModel stack)
        {
            this.SetHorizontalExpandedStack(ReferenceEquals(this._horizontalExpandedStack, stack) ? null : stack);
        }
    }

    private void OnVerticalStackHeaderClick(object sender, RoutedEventArgs args)
    {
        var root = FindStackRoot(sender as DependencyObject);
        if (root?.FindName("OrganizerPanel") is not FrameworkElement organizer)
        {
            return;
        }

        var expand = organizer.Visibility != Visibility.Visible;
        if (expand && this._expandedVerticalOrganizer is { } previous && !ReferenceEquals(previous, root))
        {
            SetStackExpansionVisual(previous, false);
        }

        SetStackExpansionVisual(root, expand);
        this._expandedVerticalOrganizer = expand ? root : null;
    }

    private void OnVerticalStackHeaderPointerEntered(
        object sender,
        PointerRoutedEventArgs args) =>
        SetVerticalStackHeaderHover(sender as FrameworkElement, true);

    private void OnVerticalStackHeaderPointerExited(
        object sender,
        PointerRoutedEventArgs args) =>
        SetVerticalStackHeaderHover(sender as FrameworkElement, false);

    private static void SetVerticalStackHeaderHover(
        FrameworkElement? header,
        bool isPointerOver)
    {
        if (header?.FindName("StackHeaderHoverBackground") is Border hoverBackground)
        {
            hoverBackground.Opacity = isPointerOver ? 1 : 0;
        }

        if (header?.FindName("PopOutButton") is Button button)
        {
            button.Opacity = isPointerOver ? 0.92 : 0;
            button.IsHitTestVisible = isPointerOver;
        }
    }

    private void OnStackPointerEntered(object sender, PointerRoutedEventArgs args) =>
        SetPopOutOpacity(sender as FrameworkElement, 0.92);

    private void OnStackPointerExited(object sender, PointerRoutedEventArgs args)
    {
        if (sender is not FrameworkElement root ||
            root.FindName("PopOutButton") is not Button { FocusState: FocusState.Unfocused } button)
        {
            return;
        }

        button.Opacity = root.FindName("OrganizerPanel") is FrameworkElement { Visibility: Visibility.Visible }
            ? 0.92
            : this.Side.IsVertical()
                ? 0.22
                : 0.16;
    }

    private void OnPopOutButtonGotFocus(object sender, RoutedEventArgs args)
    {
        if (sender is Button button)
        {
            button.Opacity = 0.92;
        }
    }

    private void OnPopOutButtonLostFocus(object sender, RoutedEventArgs args)
    {
        if (sender is Button button)
        {
            button.Opacity = this.Side.IsVertical() ? 0.22 : 0.16;
        }
    }

    private void OnOpenTrayMenuClick(object sender, RoutedEventArgs args)
    {
        if (GetTaggedStack(sender) is { } stack)
        {
            App.Current.OpenTray(stack);
        }
    }

    private async void OnInsertClipboardContentMenuClick(object sender, RoutedEventArgs args)
    {
        if (GetTaggedStack(sender) is { } stack)
        {
            await App.Current.InsertClipboardContentAsync(stack);
        }
    }

    private static void SetStackExpansionVisual(FrameworkElement root, bool isExpanded)
    {
        if (root.FindName("ExpandedSurface") is Border surface)
        {
            surface.Visibility = isExpanded ? Visibility.Visible : Visibility.Collapsed;
        }

        if (root.FindName("OrganizerPanel") is FrameworkElement organizer)
        {
            organizer.Visibility = isExpanded ? Visibility.Visible : Visibility.Collapsed;
        }

        if (root.FindName("ExpansionGlyph") is FontIcon glyph)
        {
            glyph.Glyph = isExpanded ? "\uE70E" : "\uE70D";
        }
    }

    private static void SetPopOutOpacity(FrameworkElement? root, double opacity)
    {
        if (root?.FindName("PopOutButton") is Button button)
        {
            button.Opacity = opacity;
        }
    }

    private void SetHorizontalExpandedStack(DropStackViewModel? stack)
    {
        if (ReferenceEquals(this._horizontalExpandedStack, stack))
        {
            this.UpdateHorizontalDetailHeader();
            return;
        }

        var wasExpanded = this._horizontalExpandedStack is not null;
        if (this._horizontalExpandedStack is { } previous)
        {
            previous.ModelChanged -= this.OnHorizontalExpandedStackChanged;
        }

        this._horizontalExpandedStack = stack;
        this.HorizontalOrganizer.Stack = stack;
        this.HorizontalDetailPopOutButton.Tag = stack;
        this.HorizontalStackList.SelectedItem = stack;
        this.HorizontalDetailPanel.Visibility = stack is null ? Visibility.Collapsed : Visibility.Visible;

        if (stack is not null)
        {
            stack.ModelChanged += this.OnHorizontalExpandedStackChanged;
        }

        this.UpdateHorizontalDetailHeader();

        if (wasExpanded != stack is not null)
        {
            this.HorizontalDetailExpansionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnHorizontalExpandedStackChanged(object? sender, EventArgs args) =>
        this.UpdateHorizontalDetailHeader();

    private void UpdateHorizontalDetailHeader()
    {
        this.HorizontalDetailTitle.Text = this._horizontalExpandedStack?.Name ?? string.Empty;
        this.HorizontalDetailCount.Text = this._horizontalExpandedStack?.ItemCountText ?? string.Empty;
    }

    private void OnCloseHorizontalDetailClick(object sender, RoutedEventArgs args) =>
        this.SetHorizontalExpandedStack(null);

    private void OnCommandSurfaceExternalDragEntered(object? sender, EventArgs args) =>
        this.ExternalDragEntered?.Invoke(this, EventArgs.Empty);

    private void OnCommandSurfaceExternalDragLeft(object? sender, EventArgs args) =>
        this.ExternalDragLeft?.Invoke(this, EventArgs.Empty);

    private void OnCommandSurfaceDropCompleted(object? sender, EventArgs args) =>
        this.DropCompleted?.Invoke(this, EventArgs.Empty);

    private void OnMoveToLeftMenuClick(object sender, RoutedEventArgs args) =>
        this.MoveTaggedStack(sender, EdgeShelfSide.Left);

    private void OnMoveToRightMenuClick(object sender, RoutedEventArgs args) =>
        this.MoveTaggedStack(sender, EdgeShelfSide.Right);

    private void OnMoveToTopMenuClick(object sender, RoutedEventArgs args) =>
        this.MoveTaggedStack(sender, EdgeShelfSide.Top);

    private void OnMoveToBottomMenuClick(object sender, RoutedEventArgs args) =>
        this.MoveTaggedStack(sender, EdgeShelfSide.Bottom);

    private void MoveTaggedStack(object sender, EdgeShelfSide side)
    {
        if (GetTaggedStack(sender) is { } stack && this.ViewModel.AssignStackToEdge(stack, side))
        {
            ShowStatus($"Moved {stack.Name} to the {side.GetDisplayName().ToLowerInvariant()} edge.",
                InfoBarSeverity.Success);
        }
    }

    private void OnRemoveFromEdgeMenuClick(object sender, RoutedEventArgs args)
    {
        if (GetTaggedStack(sender) is { } stack && this.ViewModel.RemoveStackFromEdge(stack))
        {
            ShowStatus($"Hid {stack.Name} from the edge shelf.", InfoBarSeverity.Success);
        }
    }

    private void OnScrollZoneDragOver(object sender, DragEventArgs args)
    {
        args.Handled = true;
        this.ExternalDragEntered?.Invoke(this, EventArgs.Empty);
        if (!DragDropDataService.HasStackReference(args.DataView) &&
            !DragDropDataService.HasSupportedFormat(args.DataView))
        {
            args.AcceptedOperation = DataPackageOperation.None;
            return;
        }

        args.AcceptedOperation = DataPackageOperation.Copy;
        var direction = (sender as FrameworkElement)?.Tag as string == "Leading" ? -1d : 1d;
        var scrollViewer = this._stackScrollViewer ??= FindDescendant<ScrollViewer>(this._activeStackList);
        if (scrollViewer is not null)
        {
            if (this.Side.IsVertical())
            {
                var offset = Math.Clamp(scrollViewer.VerticalOffset + (direction * 36), 0,
                    scrollViewer.ScrollableHeight);
                scrollViewer.ChangeView(null, offset, null, true);
            }
            else
            {
                var offset = Math.Clamp(scrollViewer.HorizontalOffset + (direction * 72), 0,
                    scrollViewer.ScrollableWidth);
                scrollViewer.ChangeView(offset, null, null, true);
            }
        }

        args.DragUIOverride.Caption = direction < 0 ? "Scroll toward start" : "Scroll toward end";
    }

    private void OnCollapseClick(object sender, RoutedEventArgs args) =>
        this.CollapseRequested?.Invoke(this, EventArgs.Empty);

    private void OnPointerEntered(object sender, PointerRoutedEventArgs args) =>
        this.PointerInteractionStarted?.Invoke(this, EventArgs.Empty);

    private void OnPointerExited(object sender, PointerRoutedEventArgs args) =>
        this.PointerInteractionEnded?.Invoke(this, EventArgs.Empty);

    private async Task AddDropToEdgeAsync(DataPackageView dataView, bool copy)
    {
        try
        {
            var referencedStackId = await DragDropDataService.ReadStackReferenceAsync(dataView);
            if (referencedStackId is { } stackId)
            {
                var stack = this.ViewModel.Stacks.FirstOrDefault(candidate => candidate.Model.Id == stackId);
                if (stack is null)
                {
                    ShowStatus("That stack is no longer available.", InfoBarSeverity.Warning);
                }
                else if (this.ViewModel.AssignStackToEdge(stack, this.Side))
                {
                    ShowStatus($"Moved {stack.Name} to the {this.Side.GetDisplayName().ToLowerInvariant()} edge.",
                        InfoBarSeverity.Success);
                }

                return;
            }

            var itemReference = await DragDropDataService.ReadItemReferenceAsync(dataView);
            if (itemReference is not null)
            {
                var source
                    = this.ViewModel.Stacks.FirstOrDefault(stack => stack.Model.Id == itemReference.SourceStackId);
                var selectedIds = itemReference.ItemIds.ToHashSet();
                var selectedItems = source?.Model.Items
                    .Where(item => selectedIds.Contains(item.Id))
                    .ToArray();
                if (source is null || selectedItems is null || selectedItems.Length != selectedIds.Count)
                {
                    ShowStatus("Those items are no longer available.", InfoBarSeverity.Warning);
                    return;
                }

                var name = selectedItems.Length == 1
                    ? selectedItems[0].DisplayName
                    : $"{selectedItems.Length} items";
                var created = this.ViewModel.AddStack(DropStack.CreateEmpty(name, source.Tint));
                this.ViewModel.AssignStackToEdge(created, this.Side);
                try
                {
                    if (!await App.Current.TransferItemsAsync(itemReference, created, 0, copy))
                    {
                        this.ViewModel.RemoveStack(created);
                        ShowStatus("The drop did not create a stack.", InfoBarSeverity.Warning);
                        return;
                    }

                    ShowStatus(
                        $"{(copy ? "Copied" : "Moved")} {selectedItems.Length} {(selectedItems.Length == 1 ? "item" : "items")} into a new {this.Side.GetDisplayName().ToLowerInvariant()} edge stack.",
                        InfoBarSeverity.Success);
                }
                catch
                {
                    this.ViewModel.RemoveStack(created);
                    throw;
                }

                return;
            }

            var items = await DragDropDataService.ReadAsync(dataView);
            if (items.Count == 0)
            {
                ShowStatus("This drag did not contain a supported payload.", InfoBarSeverity.Warning);
                return;
            }

            var createdStack = this.ViewModel.AddStack(DropStack.Create(items));
            this.ViewModel.AssignStackToEdge(createdStack, this.Side);
            ShowStatus(
                items.Count == 1
                    ? $"Created a {this.Side.GetDisplayName().ToLowerInvariant()} edge stack with 1 item."
                    : $"Created a {this.Side.GetDisplayName().ToLowerInvariant()} edge stack with {items.Count} items.",
                InfoBarSeverity.Success);
        }
        catch (Exception exception)
        {
            ShowStatus($"The drop could not be captured: {exception.Message}", InfoBarSeverity.Error);
        }
    }

    private static bool ConfigureContentDragOver(DragEventArgs args, string caption)
    {
        if (!DragDropDataService.HasSupportedFormat(args.DataView))
        {
            args.AcceptedOperation = DataPackageOperation.None;
            return false;
        }

        args.AcceptedOperation = DataPackageOperation.Copy;
        args.DragUIOverride.Caption = caption;
        args.DragUIOverride.IsCaptionVisible = true;
        args.DragUIOverride.IsContentVisible = true;
        return true;
    }

    private static void ConfigureItemTransferDragOver(
        DragEventArgs args,
        string caption,
        DropStackViewModel? target = null)
    {
        var sameStack = target is not null &&
                        DragDropDataService.ActiveItemReference?.SourceStackId == target.Model.Id;
        if (sameStack)
        {
            args.AcceptedOperation = DataPackageOperation.None;
            args.DragUIOverride.Caption = "Item is already in this stack";
            args.DragUIOverride.IsCaptionVisible = true;
            args.DragUIOverride.IsContentVisible = true;
            return;
        }

        var copy = !sameStack && IsCopyRequested(args);
        args.AcceptedOperation = copy
            ? DataPackageOperation.Copy
            : DragDropDataService.GetAcceptedInternalMoveOperation(args.DataView);
        args.DragUIOverride.Caption = copy ? $"Copy — {caption}" : caption;
        args.DragUIOverride.IsCaptionVisible = true;
        args.DragUIOverride.IsContentVisible = true;
    }

    private static bool IsCopyRequested(DragEventArgs args) =>
        (args.Modifiers & DragDropModifiers.Control) != 0;

    private static void ShowDropImportStatus(string stackName, int candidateCount, int addedCount)
    {
        var skippedCount = candidateCount - addedCount;
        if (addedCount == 0)
        {
            ShowStatus(
                $"No items were added to {stackName}; the filesystem items are already in this stack.",
                InfoBarSeverity.Informational);
            return;
        }

        var message = skippedCount == 0
            ? addedCount == 1
                ? $"Added 1 item to {stackName}."
                : $"Added {addedCount} items to {stackName}."
            : $"Added {addedCount} {(addedCount == 1 ? "item" : "items")} to {stackName} and skipped " +
              $"{skippedCount} already-present filesystem {(skippedCount == 1 ? "item" : "items")}.";
        ShowStatus(message, InfoBarSeverity.Success);
    }

    private async Task TransferItemsIntoStackAsync(
        DataPackageView dataView,
        DropStackViewModel target,
        int targetIndex,
        bool copy)
    {
        var itemReference = await DragDropDataService.ReadItemReferenceAsync(dataView);
        if (itemReference is null)
        {
            ShowStatus("Those items are no longer available.", InfoBarSeverity.Warning);
            return;
        }

        copy = copy && itemReference.SourceStackId != target.Model.Id;

        try
        {
            if (!await App.Current.TransferItemsAsync(itemReference, target, targetIndex, copy))
            {
                ShowStatus("The drop did not change the stack.", InfoBarSeverity.Informational);
                return;
            }

            ShowStatus(
                $"{(copy ? "Copied" : "Moved")} {itemReference.ItemIds.Count} {(itemReference.ItemIds.Count == 1 ? "item" : "items")} to {target.Name}.",
                InfoBarSeverity.Success);
        }
        catch (Exception exception)
        {
            ShowStatus($"The items could not be organized: {exception.Message}", InfoBarSeverity.Error);
        }
    }

    private void OnEdgeStacksChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (this._horizontalExpandedStack is { } expandedStack && !this.EdgeStacks.Contains(expandedStack))
        {
            this.SetHorizontalExpandedStack(null);
        }

        this.UpdateEmptyState();
    }

    private void UpdateEmptyState()
    {
        var hasStacks = this.EdgeStacks.Count > 0;
        this.VerticalEmptyState.Visibility = hasStacks ? Visibility.Collapsed : Visibility.Visible;
        this.EdgeStackList.Visibility = hasStacks ? Visibility.Visible : Visibility.Collapsed;
        this.HorizontalEmptyState.Visibility = hasStacks ? Visibility.Collapsed : Visibility.Visible;
        this.HorizontalStackList.Visibility = hasStacks ? Visibility.Visible : Visibility.Collapsed;

        if (!hasStacks)
        {
            this.SetHorizontalExpandedStack(null);
        }
    }

    private void ShowDragScrollZones()
    {
        if (this.EdgeStacks.Count < 2)
        {
            return;
        }

        this.LeadingScrollZone.Visibility = Visibility.Visible;
        this.TrailingScrollZone.Visibility = Visibility.Visible;
    }

    private void HideDragScrollZones()
    {
        this.LeadingScrollZone.Visibility = Visibility.Collapsed;
        this.TrailingScrollZone.Visibility = Visibility.Collapsed;
    }

    private static DropStackViewModel? GetTaggedStack(object sender) =>
        (sender as FrameworkElement)?.Tag as DropStackViewModel;

    private static FrameworkElement? FindStackRoot(DependencyObject? element)
    {
        while (element is not null)
        {
            if (element is FrameworkElement { Name: "StackRoot" } root)
            {
                return root;
            }

            element = VisualTreeHelper.GetParent(element);
        }

        return null;
    }

    private static void SetStackDropOutline(object sender, bool isVisible)
    {
        if (sender is FrameworkElement element &&
            element.FindName("StackDropOutline") is Border outline)
        {
            outline.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private static T? FindDescendant<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
            {
                return match;
            }

            if (FindDescendant<T>(child) is { } descendant)
            {
                return descendant;
            }
        }

        return null;
    }

    private static void ShowStatus(string message, InfoBarSeverity severity) =>
        App.Current.ShowToast(message, severity);
}
