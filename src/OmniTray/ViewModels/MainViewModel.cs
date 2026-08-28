// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Windows.UI;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace OmniTray.ViewModels;

public partial class MainViewModel : BaseViewModel
{
    private bool _bottomEdgeWindowEnabled = true;
    private EdgeWindowAlignment _bottomEdgeWindowAlignment = EdgeWindowAlignment.Center;
    private EdgeWindowSizeMode _bottomEdgeWindowSizeMode = EdgeWindowSizeMode.Reasonable;
    private bool _catalogChangePending;
    private int _catalogMutationDepth;
    private bool _edgeWindowsPaused;
    private bool _gameModeEnabled = true;
    private string _gameModeStatusText = "Game mode is ready.";
    private StackCardDisplayMode _horizontalStackCardDisplayMode = StackCardDisplayMode.LargeList;
    private bool _isGameModeSuppressing;
    private bool _isRestoring;
    private bool _leftEdgeWindowEnabled = true;
    private EdgeWindowAlignment _leftEdgeWindowAlignment = EdgeWindowAlignment.Center;
    private EdgeWindowSizeMode _leftEdgeWindowSizeMode = EdgeWindowSizeMode.Reasonable;
    private bool _rightEdgeWindowEnabled = true;
    private EdgeWindowAlignment _rightEdgeWindowAlignment = EdgeWindowAlignment.Center;
    private EdgeWindowSizeMode _rightEdgeWindowSizeMode = EdgeWindowSizeMode.Reasonable;
    private bool _syncAllEdgeContent;
    private bool _syncLeftAndRightEdgeContent;
    private bool _syncTopAndBottomEdgeContent;
    private bool _topEdgeWindowEnabled = true;
    private EdgeWindowAlignment _topEdgeWindowAlignment = EdgeWindowAlignment.Center;
    private EdgeWindowSizeMode _topEdgeWindowSizeMode = EdgeWindowSizeMode.Reasonable;
    private StackCardDisplayMode _verticalStackCardDisplayMode = StackCardDisplayMode.LargeList;

    public MainViewModel()
        : base("OmniTray")
    {
        this.Stacks.CollectionChanged += this.OnStacksChanged;
        this.LeftEdgeStacks.CollectionChanged += (_, args) =>
            this.OnEdgeStacksChanged(EdgeShelfSide.Left, this.LeftEdgeStacks, args);
        this.RightEdgeStacks.CollectionChanged += (_, args) =>
            this.OnEdgeStacksChanged(EdgeShelfSide.Right, this.RightEdgeStacks, args);
        this.TopEdgeStacks.CollectionChanged
            += (_, args) => this.OnEdgeStacksChanged(EdgeShelfSide.Top, this.TopEdgeStacks, args);
        this.BottomEdgeStacks.CollectionChanged += (_, args) =>
            this.OnEdgeStacksChanged(EdgeShelfSide.Bottom, this.BottomEdgeStacks, args);
    }

    [ObservableProperty]
    public partial string PopupTitle { get; set; } = "OmniTray";

    public ObservableCollection<DropStackViewModel> Stacks { get; } = [];

    public ObservableCollection<DropStackViewModel> LeftEdgeStacks { get; } = [];

    public ObservableCollection<DropStackViewModel> RightEdgeStacks { get; } = [];

    public ObservableCollection<DropStackViewModel> TopEdgeStacks { get; } = [];

    public ObservableCollection<DropStackViewModel> BottomEdgeStacks { get; } = [];

    public string StackCountText =>
        this.Stacks.Count switch
        {
            0 => "Empty",
            1 => "1 stack",
            _ => $"{this.Stacks.Count} stacks"
        };

    public bool IsEmpty => this.Stacks.Count == 0;

    public bool HasStacks => this.Stacks.Count > 0;

    public bool HasEdgeStacks => this.EnumerateEdgeCollections().Any(static collection => collection.Count > 0);

    public bool HasEnabledEdgeWindows =>
        this.LeftEdgeWindowEnabled ||
        this.RightEdgeWindowEnabled ||
        this.TopEdgeWindowEnabled ||
        this.BottomEdgeWindowEnabled;

    public StackCardDisplayMode VerticalStackCardDisplayMode
    {
        get => this._verticalStackCardDisplayMode;
        set
        {
            if (!this.SetProperty(ref this._verticalStackCardDisplayMode, value))
            {
                return;
            }

            foreach (var stack in this.Stacks)
            {
                stack.VerticalStackCardDisplayMode = value;
            }
        }
    }

    public StackCardDisplayMode HorizontalStackCardDisplayMode
    {
        get => this._horizontalStackCardDisplayMode;
        set
        {
            if (!this.SetProperty(ref this._horizontalStackCardDisplayMode, value))
            {
                return;
            }

            foreach (var stack in this.Stacks)
            {
                stack.HorizontalStackCardDisplayMode = value;
            }

            this.OnPropertyChanged(nameof(this.HorizontalStackCardLayout));
        }
    }

    public StackCardLayoutMetrics HorizontalStackCardLayout =>
        StackCardLayoutMetrics.Resolve(this.HorizontalStackCardDisplayMode);

    public bool EdgeWindowsPaused
    {
        get => this._edgeWindowsPaused;
        set => this.SetProperty(ref this._edgeWindowsPaused, value);
    }

    public bool GameModeEnabled
    {
        get => this._gameModeEnabled;
        set => this.SetProperty(ref this._gameModeEnabled, value);
    }

    public bool IsGameModeSuppressing
    {
        get => this._isGameModeSuppressing;
        private set => this.SetProperty(ref this._isGameModeSuppressing, value);
    }

    public string GameModeStatusText
    {
        get => this._gameModeStatusText;
        private set => this.SetProperty(ref this._gameModeStatusText, value);
    }

    public bool LeftEdgeWindowEnabled
    {
        get => this._leftEdgeWindowEnabled;
        set
        {
            if (this.SetProperty(ref this._leftEdgeWindowEnabled, value))
            {
                this.OnPropertyChanged(nameof(this.HasEnabledEdgeWindows));
            }
        }
    }

    public bool RightEdgeWindowEnabled
    {
        get => this._rightEdgeWindowEnabled;
        set
        {
            if (this.SetProperty(ref this._rightEdgeWindowEnabled, value))
            {
                this.OnPropertyChanged(nameof(this.HasEnabledEdgeWindows));
            }
        }
    }

    public bool TopEdgeWindowEnabled
    {
        get => this._topEdgeWindowEnabled;
        set
        {
            if (this.SetProperty(ref this._topEdgeWindowEnabled, value))
            {
                this.OnPropertyChanged(nameof(this.HasEnabledEdgeWindows));
            }
        }
    }

    public bool BottomEdgeWindowEnabled
    {
        get => this._bottomEdgeWindowEnabled;
        set
        {
            if (this.SetProperty(ref this._bottomEdgeWindowEnabled, value))
            {
                this.OnPropertyChanged(nameof(this.HasEnabledEdgeWindows));
            }
        }
    }

    public EdgeWindowSizeMode LeftEdgeWindowSizeMode
    {
        get => this._leftEdgeWindowSizeMode;
        set => this.SetProperty(ref this._leftEdgeWindowSizeMode, value);
    }

    public EdgeWindowAlignment LeftEdgeWindowAlignment
    {
        get => this._leftEdgeWindowAlignment;
        set => this.SetProperty(ref this._leftEdgeWindowAlignment, value);
    }

    public EdgeWindowSizeMode RightEdgeWindowSizeMode
    {
        get => this._rightEdgeWindowSizeMode;
        set => this.SetProperty(ref this._rightEdgeWindowSizeMode, value);
    }

    public EdgeWindowAlignment RightEdgeWindowAlignment
    {
        get => this._rightEdgeWindowAlignment;
        set => this.SetProperty(ref this._rightEdgeWindowAlignment, value);
    }

    public EdgeWindowSizeMode TopEdgeWindowSizeMode
    {
        get => this._topEdgeWindowSizeMode;
        set => this.SetProperty(ref this._topEdgeWindowSizeMode, value);
    }

    public EdgeWindowAlignment TopEdgeWindowAlignment
    {
        get => this._topEdgeWindowAlignment;
        set => this.SetProperty(ref this._topEdgeWindowAlignment, value);
    }

    public EdgeWindowSizeMode BottomEdgeWindowSizeMode
    {
        get => this._bottomEdgeWindowSizeMode;
        set => this.SetProperty(ref this._bottomEdgeWindowSizeMode, value);
    }

    public EdgeWindowAlignment BottomEdgeWindowAlignment
    {
        get => this._bottomEdgeWindowAlignment;
        set => this.SetProperty(ref this._bottomEdgeWindowAlignment, value);
    }

    public bool SyncLeftAndRightEdgeContent
    {
        get => this._syncLeftAndRightEdgeContent;
        set
        {
            if (this.SetProperty(ref this._syncLeftAndRightEdgeContent, value))
            {
                this.ReconcileEdgeContentSharing();
            }
        }
    }

    public bool SyncTopAndBottomEdgeContent
    {
        get => this._syncTopAndBottomEdgeContent;
        set
        {
            if (this.SetProperty(ref this._syncTopAndBottomEdgeContent, value))
            {
                this.ReconcileEdgeContentSharing();
            }
        }
    }

    public bool SyncAllEdgeContent
    {
        get => this._syncAllEdgeContent;
        set
        {
            if (this.SetProperty(ref this._syncAllEdgeContent, value))
            {
                this.OnPropertyChanged(nameof(this.CanConfigurePairedEdgeContentSync));
                this.ReconcileEdgeContentSharing();
            }
        }
    }

    public bool CanConfigurePairedEdgeContentSync => !this.SyncAllEdgeContent;

    public event EventHandler? CatalogChanged;

    public ObservableCollection<DropStackViewModel> GetEdgeStacks(EdgeShelfSide side)
    {
        var contentSource = EdgeContentSharingPolicy.ResolveContentSource(
            side,
            this.SyncLeftAndRightEdgeContent,
            this.SyncTopAndBottomEdgeContent,
            this.SyncAllEdgeContent);
        return this.GetStoredEdgeStacks(contentSource);
    }

    public bool IsEdgeWindowEnabled(EdgeShelfSide side) => side switch
    {
        EdgeShelfSide.Left => this.LeftEdgeWindowEnabled,
        EdgeShelfSide.Right => this.RightEdgeWindowEnabled,
        EdgeShelfSide.Top => this.TopEdgeWindowEnabled,
        EdgeShelfSide.Bottom => this.BottomEdgeWindowEnabled,
        _ => throw new ArgumentOutOfRangeException(nameof(side))
    };

    public void SetEdgeWindowEnabled(EdgeShelfSide side, bool enabled)
    {
        switch (side)
        {
            case EdgeShelfSide.Left:
                this.LeftEdgeWindowEnabled = enabled;
                break;
            case EdgeShelfSide.Right:
                this.RightEdgeWindowEnabled = enabled;
                break;
            case EdgeShelfSide.Top:
                this.TopEdgeWindowEnabled = enabled;
                break;
            case EdgeShelfSide.Bottom:
                this.BottomEdgeWindowEnabled = enabled;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(side));
        }
    }

    public EdgeWindowSizeMode GetEdgeWindowSizeMode(EdgeShelfSide side) => side switch
    {
        EdgeShelfSide.Left => this.LeftEdgeWindowSizeMode,
        EdgeShelfSide.Right => this.RightEdgeWindowSizeMode,
        EdgeShelfSide.Top => this.TopEdgeWindowSizeMode,
        EdgeShelfSide.Bottom => this.BottomEdgeWindowSizeMode,
        _ => throw new ArgumentOutOfRangeException(nameof(side))
    };

    public void SetEdgeWindowSizeMode(EdgeShelfSide side, EdgeWindowSizeMode sizeMode)
    {
        switch (side)
        {
            case EdgeShelfSide.Left:
                this.LeftEdgeWindowSizeMode = sizeMode;
                break;
            case EdgeShelfSide.Right:
                this.RightEdgeWindowSizeMode = sizeMode;
                break;
            case EdgeShelfSide.Top:
                this.TopEdgeWindowSizeMode = sizeMode;
                break;
            case EdgeShelfSide.Bottom:
                this.BottomEdgeWindowSizeMode = sizeMode;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(side));
        }
    }

    public EdgeWindowAlignment GetEdgeWindowAlignment(EdgeShelfSide side) => side switch
    {
        EdgeShelfSide.Left => this.LeftEdgeWindowAlignment,
        EdgeShelfSide.Right => this.RightEdgeWindowAlignment,
        EdgeShelfSide.Top => this.TopEdgeWindowAlignment,
        EdgeShelfSide.Bottom => this.BottomEdgeWindowAlignment,
        _ => throw new ArgumentOutOfRangeException(nameof(side))
    };

    public void SetEdgeWindowAlignment(EdgeShelfSide side, EdgeWindowAlignment alignment)
    {
        switch (side)
        {
            case EdgeShelfSide.Left:
                this.LeftEdgeWindowAlignment = alignment;
                break;
            case EdgeShelfSide.Right:
                this.RightEdgeWindowAlignment = alignment;
                break;
            case EdgeShelfSide.Top:
                this.TopEdgeWindowAlignment = alignment;
                break;
            case EdgeShelfSide.Bottom:
                this.BottomEdgeWindowAlignment = alignment;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(side));
        }
    }

    internal IReadOnlyList<EdgeShelfState> GetEdgeShelfStates() =>
        Enum.GetValues<EdgeShelfSide>()
            .Select(side => new EdgeShelfState(
                side, this.GetStoredEdgeStacks(side).Select(static stack => stack.Model.Id).ToArray()))
            .ToArray();

    public DropStackViewModel AddStack(DropStack stack)
    {
        var stackViewModel = this.CreateStackViewModel(stack);
        this.Stacks.Insert(0, stackViewModel);
        return stackViewModel;
    }

    public bool CanMoveStack(DropStackViewModel stack, int insertionIndex)
    {
        ArgumentNullException.ThrowIfNull(stack);
        var sourceIndex = this.Stacks.IndexOf(stack);
        return sourceIndex >= 0 &&
               ReorderOperations.WouldMove(
                   sourceIndex,
                   Math.Clamp(insertionIndex, 0, this.Stacks.Count), this.Stacks.Count);
    }

    public bool MoveStack(DropStackViewModel stack, int insertionIndex)
    {
        if (!this.CanMoveStack(stack, insertionIndex))
        {
            return false;
        }

        var sourceIndex = this.Stacks.IndexOf(stack);
        var destinationIndex = ReorderOperations.ResolveDestinationIndex(
            sourceIndex,
            Math.Clamp(insertionIndex, 0, this.Stacks.Count), this.Stacks.Count);
        this.Stacks.Move(sourceIndex, destinationIndex);
        return true;
    }

    public void RestoreStacks(IEnumerable<DropStack> stacks)
    {
        ArgumentNullException.ThrowIfNull(stacks);

        this._isRestoring = true;
        try
        {
            foreach (var stack in this.Stacks)
            {
                stack.ModelChanged -= this.OnStackModelChanged;
            }

            this.ClearEdgeCollections();
            this.Stacks.Clear();
            foreach (var stack in stacks)
            {
                this.Stacks.Add(this.CreateStackViewModel(stack));
            }
        }
        finally
        {
            this._isRestoring = false;
        }
    }

    internal void RestoreEdgeShelves(IEnumerable<EdgeShelfState> shelfStates)
    {
        ArgumentNullException.ThrowIfNull(shelfStates);

        this._isRestoring = true;
        try
        {
            foreach (var stack in this.Stacks)
            {
                stack.SetEdgeMembership(null);
            }

            this.ClearEdgeCollections();
            var stacksById = this.Stacks.ToDictionary(static stack => stack.Model.Id);
            var statesBySide = shelfStates
                .Where(static state => Enum.IsDefined(state.Side))
                .GroupBy(static state => state.Side)
                .ToDictionary(static group => group.Key, static group => group.Last());
            var assignedStackIds = new HashSet<Guid>();
            foreach (var side in Enum.GetValues<EdgeShelfSide>())
            {
                if (!statesBySide.TryGetValue(side, out var state))
                {
                    continue;
                }

                var collection = this.GetStoredEdgeStacks(side);
                foreach (var stackId in state.StackIds)
                {
                    if (assignedStackIds.Add(stackId) && stacksById.TryGetValue(stackId, out var stack))
                    {
                        collection.Add(stack);
                        stack.SetEdgeMembership(side);
                    }
                }
            }

            this.ReconcileEdgeContentSharing();
        }
        finally
        {
            this._isRestoring = false;
        }
    }

    public bool AssignStackToEdge(DropStackViewModel stack, EdgeShelfSide side)
    {
        ArgumentNullException.ThrowIfNull(stack);
        if (!this.Stacks.Contains(stack) || !Enum.IsDefined(side))
        {
            return false;
        }

        var contentSource = EdgeContentSharingPolicy.ResolveContentSource(
            side,
            this.SyncLeftAndRightEdgeContent,
            this.SyncTopAndBottomEdgeContent,
            this.SyncAllEdgeContent);
        var target = this.GetStoredEdgeStacks(contentSource);
        if (target.Contains(stack) && stack.AssignedEdge == contentSource)
        {
            return false;
        }

        this.BeginCatalogMutation();
        try
        {
            foreach (var collection in this.EnumerateEdgeCollections())
            {
                collection.Remove(stack);
            }

            target.Add(stack);
            return true;
        }
        finally
        {
            this.EndCatalogMutation();
        }
    }

    public bool CanMoveStackToEdge(
        DropStackViewModel stack,
        EdgeShelfSide side,
        int insertionIndex)
    {
        ArgumentNullException.ThrowIfNull(stack);
        if (!this.Stacks.Contains(stack) || !Enum.IsDefined(side))
        {
            return false;
        }

        var target = this.GetEdgeStacks(side);
        var sourceIndex = target.IndexOf(stack);
        return sourceIndex < 0 ||
               ReorderOperations.WouldMove(
                   sourceIndex,
                   Math.Clamp(insertionIndex, 0, target.Count),
                   target.Count);
    }

    public bool MoveStackToEdge(
        DropStackViewModel stack,
        EdgeShelfSide side,
        int insertionIndex)
    {
        if (!this.CanMoveStackToEdge(stack, side, insertionIndex))
        {
            return false;
        }

        var target = this.GetEdgeStacks(side);
        var sourceIndex = target.IndexOf(stack);
        if (sourceIndex >= 0)
        {
            var destinationIndex = ReorderOperations.ResolveDestinationIndex(
                sourceIndex,
                Math.Clamp(insertionIndex, 0, target.Count),
                target.Count);
            target.Move(sourceIndex, destinationIndex);
            return true;
        }

        this.BeginCatalogMutation();
        try
        {
            foreach (var collection in this.EnumerateEdgeCollections())
            {
                collection.Remove(stack);
            }

            target.Insert(Math.Clamp(insertionIndex, 0, target.Count), stack);
            return true;
        }
        finally
        {
            this.EndCatalogMutation();
        }
    }

    public bool RemoveStackFromEdge(DropStackViewModel stack)
    {
        ArgumentNullException.ThrowIfNull(stack);
        var removed = false;
        this.BeginCatalogMutation();
        try
        {
            foreach (var collection in this.EnumerateEdgeCollections())
            {
                removed |= collection.Remove(stack);
            }

            return removed;
        }
        finally
        {
            this.EndCatalogMutation();
        }
    }

    public bool RemoveStack(DropStackViewModel stack)
    {
        ArgumentNullException.ThrowIfNull(stack);
        if (!this.Stacks.Contains(stack))
        {
            return false;
        }

        this.BeginCatalogMutation();
        try
        {
            this.RemoveStackFromEdge(stack);
            stack.ModelChanged -= this.OnStackModelChanged;
            return this.Stacks.Remove(stack);
        }
        finally
        {
            this.EndCatalogMutation();
        }
    }

    public int RemoveEmptyStacks()
    {
        var emptyStacks = this.Stacks
            .Where(static stack => stack.Model.Items.Count == 0)
            .ToArray();
        if (emptyStacks.Length == 0)
        {
            return 0;
        }

        this.BeginCatalogMutation();
        try
        {
            foreach (var stack in emptyStacks)
            {
                this.RemoveStack(stack);
            }
        }
        finally
        {
            this.EndCatalogMutation();
        }

        return emptyStacks.Length;
    }

    public DropStackViewModel SplitStack(
        DropStackViewModel source,
        IEnumerable<Guid> selectedItemIds)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(selectedItemIds);
        var sourceIndex = this.Stacks.IndexOf(source);
        if (sourceIndex < 0)
        {
            throw new ArgumentException("The source stack is not in this catalog.", nameof(source));
        }

        var (remaining, extracted) = StackOperations.Split(source.Model, selectedItemIds);
        var extractedViewModel = this.CreateStackViewModel(extracted);
        var edgeSide = source.AssignedEdge;
        var edgeCollection = edgeSide is { } side ? this.GetEdgeStacks(side) : null;
        var edgeIndex = edgeCollection?.IndexOf(source) ?? -1;
        this.BeginCatalogMutation();
        try
        {
            source.ReplaceModel(remaining);
            this.Stacks.Insert(sourceIndex + 1, extractedViewModel);
            if (edgeCollection is not null && edgeIndex >= 0)
            {
                edgeCollection.Insert(edgeIndex + 1, extractedViewModel);
            }
        }
        finally
        {
            this.EndCatalogMutation();
        }

        return extractedViewModel;
    }

    public bool CombineStacks(DropStackViewModel target, DropStackViewModel source)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(source);
        if (ReferenceEquals(target, source) || !this.Stacks.Contains(target) || !this.Stacks.Contains(source))
        {
            return false;
        }

        var combined = StackOperations.CombineInto(target.Model, [source.Model]);
        this.BeginCatalogMutation();
        try
        {
            target.ReplaceModel(combined);
            this.RemoveStackFromEdge(source);
            source.ModelChanged -= this.OnStackModelChanged;
            this.Stacks.Remove(source);
        }
        finally
        {
            this.EndCatalogMutation();
        }

        return true;
    }

    public bool MoveItems(
        DropStackViewModel source,
        DropStackViewModel target,
        IEnumerable<Guid> itemIds,
        int targetIndex)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(itemIds);
        if (!this.Stacks.Contains(source) || !this.Stacks.Contains(target))
        {
            return false;
        }

        if (ReferenceEquals(source, target))
        {
            var reordered = StackOperations.MoveItemsWithin(
                source.Model,
                itemIds,
                Math.Clamp(targetIndex, 0, source.Model.Items.Count));
            if (reordered.Items.Select(static item => item.Id).SequenceEqual(
                    source.Model.Items.Select(static item => item.Id)))
            {
                return false;
            }

            source.ReplaceModel(reordered);
            return true;
        }

        var moved = StackOperations.MoveItems(
            source.Model,
            target.Model,
            itemIds,
            Math.Clamp(targetIndex, 0, target.Model.Items.Count));
        this.BeginCatalogMutation();
        try
        {
            source.ReplaceModel(moved.Source);
            target.ReplaceModel(moved.Target);
        }
        finally
        {
            this.EndCatalogMutation();
        }

        return true;
    }

    public bool InsertItems(
        DropStackViewModel target,
        IEnumerable<DropItem> items,
        int targetIndex)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(items);
        if (!this.Stacks.Contains(target))
        {
            return false;
        }

        target.ReplaceModel(StackOperations.InsertItems(
            target.Model,
            items,
            Math.Clamp(targetIndex, 0, target.Model.Items.Count)));
        return true;
    }

    public void ClearStacks()
    {
        this.BeginCatalogMutation();
        try
        {
            foreach (var stack in this.Stacks)
            {
                stack.ModelChanged -= this.OnStackModelChanged;
            }

            this.ClearEdgeCollections();
            this.Stacks.Clear();
        }
        finally
        {
            this.EndCatalogMutation();
        }
    }

    [RelayCommand(CanExecute = nameof(CanClear))]
    private void Clear() => this.ClearStacks();

    private bool CanClear() => this.Stacks.Count > 0;

    private void OnStacksChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        this.OnPropertyChanged(nameof(this.StackCountText));
        this.OnPropertyChanged(nameof(this.IsEmpty));
        this.OnPropertyChanged(nameof(this.HasStacks));
        this.ClearCommand.NotifyCanExecuteChanged();
        this.RequestCatalogChange();
    }

    private void OnEdgeStacksChanged(
        EdgeShelfSide side,
        ObservableCollection<DropStackViewModel> collection,
        NotifyCollectionChangedEventArgs args)
    {
        if (args.Action != NotifyCollectionChangedAction.Move)
        {
            if (args.OldItems is not null)
            {
                foreach (var stack in args.OldItems.OfType<DropStackViewModel>())
                {
                    if (!this.EnumerateEdgeCollections().Any(candidate => candidate.Contains(stack)))
                    {
                        stack.SetEdgeMembership(null);
                    }
                }
            }

            if (args.NewItems is not null)
            {
                foreach (var stack in args.NewItems.OfType<DropStackViewModel>())
                {
                    stack.SetEdgeMembership(side);
                }
            }

            if (args.Action == NotifyCollectionChangedAction.Reset)
            {
                foreach (var stack in this.Stacks)
                {
                    stack.SetEdgeMembership(this.FindAssignedEdge(stack));
                }
            }
        }

        this.OnPropertyChanged(nameof(this.HasEdgeStacks));
        this.RequestCatalogChange();
    }

    private EdgeShelfSide? FindAssignedEdge(DropStackViewModel stack)
    {
        foreach (var side in Enum.GetValues<EdgeShelfSide>())
        {
            if (this.GetStoredEdgeStacks(side).Contains(stack))
            {
                return side;
            }
        }

        return null;
    }

    private IEnumerable<ObservableCollection<DropStackViewModel>> EnumerateEdgeCollections()
    {
        yield return this.LeftEdgeStacks;
        yield return this.RightEdgeStacks;
        yield return this.TopEdgeStacks;
        yield return this.BottomEdgeStacks;
    }

    private ObservableCollection<DropStackViewModel> GetStoredEdgeStacks(EdgeShelfSide side) => side switch
    {
        EdgeShelfSide.Left => this.LeftEdgeStacks,
        EdgeShelfSide.Right => this.RightEdgeStacks,
        EdgeShelfSide.Top => this.TopEdgeStacks,
        EdgeShelfSide.Bottom => this.BottomEdgeStacks,
        _ => throw new ArgumentOutOfRangeException(nameof(side))
    };

    private void ReconcileEdgeContentSharing()
    {
        this.BeginCatalogMutation();
        try
        {
            foreach (var side in Enum.GetValues<EdgeShelfSide>())
            {
                var contentSource = EdgeContentSharingPolicy.ResolveContentSource(
                    side,
                    this.SyncLeftAndRightEdgeContent,
                    this.SyncTopAndBottomEdgeContent,
                    this.SyncAllEdgeContent);
                if (contentSource == side)
                {
                    continue;
                }

                var source = this.GetStoredEdgeStacks(side);
                var target = this.GetStoredEdgeStacks(contentSource);
                foreach (var stack in source.ToArray())
                {
                    if (!target.Contains(stack))
                    {
                        target.Add(stack);
                    }
                }

                source.Clear();
            }
        }
        finally
        {
            this.EndCatalogMutation();
        }
    }

    private void ClearEdgeCollections()
    {
        foreach (var collection in this.EnumerateEdgeCollections())
        {
            collection.Clear();
        }
    }

    private DropStackViewModel CreateStackViewModel(DropStack stack)
    {
        var stackViewModel = new DropStackViewModel(stack)
        {
            VerticalStackCardDisplayMode = this.VerticalStackCardDisplayMode,
            HorizontalStackCardDisplayMode = this.HorizontalStackCardDisplayMode
        };
        stackViewModel.ModelChanged += this.OnStackModelChanged;
        return stackViewModel;
    }

    private void OnStackModelChanged(object? sender, EventArgs args) => this.RequestCatalogChange();

    private void BeginCatalogMutation() => this._catalogMutationDepth++;

    private void EndCatalogMutation()
    {
        this._catalogMutationDepth--;
        if (this._catalogMutationDepth == 0 && this._catalogChangePending)
        {
            this._catalogChangePending = false;
            this.PublishNoteCatalogChange();
        }
    }

    private void RequestCatalogChange()
    {
        if (this._isRestoring)
        {
            return;
        }

        if (this._catalogMutationDepth > 0)
        {
            this._catalogChangePending = true;
            return;
        }

        this.PublishNoteCatalogChange();
    }

    internal void SetGameModeStatus(bool isSuppressing, string statusText)
    {
        this.IsGameModeSuppressing = isSuppressing;
        this.GameModeStatusText = statusText;
    }

    internal void RefreshSystemColors()
    {
        foreach (var stack in this.Stacks)
        {
            stack.RefreshSystemColors();
        }
    }
}

public sealed class DropStackViewModel : ObservableObject
{
    private readonly HashSet<DropItemViewModel> _previewItems = [];
    private EdgeShelfSide? _assignedEdge;
    private StackCardDisplayMode _horizontalStackCardDisplayMode = StackCardDisplayMode.LargeList;
    private bool _isSynchronizingItems;
    private DropStack _model;
    private StackCardDisplayMode _verticalStackCardDisplayMode = StackCardDisplayMode.LargeList;

    public DropStackViewModel(DropStack model)
    {
        this._model = model;
        this.Items = new ObservableCollection<DropItemViewModel>(
            model.Items.Select(static item => new DropItemViewModel(item)));
        this.Items.CollectionChanged += this.OnItemsCollectionChanged;
        this.SynchronizePreviewSubscriptions();
        var tintColor = this.TintColor;
        this.TintBrush = new SolidColorBrush(tintColor);
        this.TintForegroundBrush = new SolidColorBrush(
            GetContrastingForeground(tintColor));
    }

    public DropStack Model
    {
        get => this._model;
        private set => this.SetProperty(ref this._model, value);
    }

    public ObservableCollection<DropItemViewModel> Items { get; }

    public StackCardDisplayMode VerticalStackCardDisplayMode
    {
        get => this._verticalStackCardDisplayMode;
        set
        {
            if (this.SetProperty(ref this._verticalStackCardDisplayMode, value))
            {
                this.OnPropertyChanged(nameof(this.CardLayout));
            }
        }
    }

    public StackCardDisplayMode HorizontalStackCardDisplayMode
    {
        get => this._horizontalStackCardDisplayMode;
        set
        {
            if (this.SetProperty(ref this._horizontalStackCardDisplayMode, value))
            {
                this.OnPropertyChanged(nameof(this.HorizontalCardLayout));
            }
        }
    }

    public StackCardLayoutMetrics CardLayout =>
        StackCardLayoutMetrics.Resolve(this.VerticalStackCardDisplayMode);

    public StackCardLayoutMetrics HorizontalCardLayout =>
        StackCardLayoutMetrics.Resolve(this.HorizontalStackCardDisplayMode);

    public StackCardLayoutMetrics ThumbnailCardLayout =>
        StackCardLayoutMetrics.Resolve(StackCardDisplayMode.ThumbnailIcon);

    public string Name => this.Model.Name;

    public string CompactName => this.Name.Length <= 12 ? this.Name : $"{this.Name[..11]}…";

    public string Tint => this.Model.Tint;

    public StackInspectorViewMode InspectorViewMode => this.Model.InspectorViewMode;

    public Color TintColor => ResolveTint(this.Model.Tint);

    public SolidColorBrush TintBrush { get; }

    public SolidColorBrush TintForegroundBrush { get; }

    public string ItemCountText => this.Model.Items.Count == 1 ? "1 item" : $"{this.Model.Items.Count} items";

    public string Summary =>
        this.Model.Items.Count == 0
            ? "Empty stack"
            : string.Join(
                " · ", this.Model.Items
                    .GroupBy(static item => item.Kind)
                    .Select(static group =>
                        $"{group.Count()} {group.Key.ToString().ToLowerInvariant()}{(group.Count() == 1 ? string.Empty : "s")}"));

    public string LeadingGlyph => this.Items.Count == 0 ? "\uE710" : this.Items[0].LeadingGlyph;

    public ImageSource? PreviewThumbnailSource => this.GetPreviewThumbnail(0);

    public Thickness PreviewThumbnailBorderThickness => this.GetPreviewBorderThickness(0);

    public Visibility PreviewThumbnailVisibility =>
        this.PreviewThumbnailSource is null
            ? Visibility.Collapsed
            : Visibility.Visible;

    public Visibility PreviewGlyphVisibility =>
        this.PreviewThumbnailSource is null
            ? Visibility.Visible
            : Visibility.Collapsed;

    public bool PreviewThumbnailIsShellIcon => this.GetPreviewItem(0)?.ThumbnailIsShellIcon ?? false;

    public bool PreviewThumbnailHasVideoFilmstrip =>
        this.GetPreviewItem(0)?.ThumbnailHasVideoFilmstrip ?? false;

    public Visibility PreviewVideoFilmstripVisibility =>
        this.PreviewThumbnailHasVideoFilmstrip ? Visibility.Visible : Visibility.Collapsed;

    public Stretch PreviewThumbnailStretch =>
        this.PreviewThumbnailIsShellIcon ? Stretch.Uniform : Stretch.UniformToFill;

    public ImageSource? SecondPreviewThumbnailSource => this.GetPreviewThumbnail(1);

    public Thickness SecondPreviewThumbnailBorderThickness => this.GetPreviewBorderThickness(1);

    public Visibility SecondPreviewThumbnailVisibility =>
        this.SecondPreviewThumbnailSource is null
            ? Visibility.Collapsed
            : Visibility.Visible;

    public bool SecondPreviewThumbnailIsShellIcon => this.GetPreviewItem(1)?.ThumbnailIsShellIcon ?? false;

    public bool SecondPreviewThumbnailHasVideoFilmstrip =>
        this.GetPreviewItem(1)?.ThumbnailHasVideoFilmstrip ?? false;

    public ImageSource? ThirdPreviewThumbnailSource => this.GetPreviewThumbnail(2);

    public Thickness ThirdPreviewThumbnailBorderThickness => this.GetPreviewBorderThickness(2);

    public Visibility ThirdPreviewThumbnailVisibility =>
        this.ThirdPreviewThumbnailSource is null
            ? Visibility.Collapsed
            : Visibility.Visible;

    public bool ThirdPreviewThumbnailIsShellIcon => this.GetPreviewItem(2)?.ThumbnailIsShellIcon ?? false;

    public bool ThirdPreviewThumbnailHasVideoFilmstrip =>
        this.GetPreviewItem(2)?.ThumbnailHasVideoFilmstrip ?? false;

    public Visibility SecondLayerVisibility => this.Items.Count >= 2 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ThirdLayerVisibility => this.Items.Count >= 3 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility CountBadgeVisibility => this.Items.Count >= 3 ? Visibility.Visible : Visibility.Collapsed;

    public string CountBadgeText => this.Items.Count >= 3 ? this.Items.Count.ToString() : string.Empty;

    public Visibility TileCountBadgeVisibility => this.Items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public string TileCountBadgeText => this.Items.Count.ToString();

    public string AccessibleName => $"{this.Name}, {this.ItemCountText}, {this.Summary}";

    public EdgeShelfSide? AssignedEdge => this._assignedEdge;

    public bool IsNotOnEdgeShelf => this.AssignedEdge is null;

    public bool IsOnLeftEdgeShelf => this.AssignedEdge == EdgeShelfSide.Left;

    public bool IsOnRightEdgeShelf => this.AssignedEdge == EdgeShelfSide.Right;

    public bool IsOnTopEdgeShelf => this.AssignedEdge == EdgeShelfSide.Top;

    public bool IsOnBottomEdgeShelf => this.AssignedEdge == EdgeShelfSide.Bottom;

    public bool IsInEdgeWindow => this.AssignedEdge is not null;

    public bool CanAddToEdgeWindow => !this.IsInEdgeWindow;

    public string EdgePlacementText =>
        this.AssignedEdge is { } side
            ? $"{side.GetDisplayName()} edge"
            : "Not on an edge";

    public event EventHandler? ModelChanged;

    public int AppendDroppedItems(IEnumerable<DropItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        var additions = DropImportDeduplication.FilterNewItems(this.Model.Items, items);
        if (additions.Count == 0)
        {
            return 0;
        }

        this.ReplaceModel(this.Model.Append(additions));
        return additions.Count;
    }

    public void Rename(string name) => this.ApplyModel(this.Model.Rename(name));

    public void ChangeTint(string tint) => this.ApplyModel(this.Model.ChangeTint(tint));

    public void ChangeInspectorViewMode(StackInspectorViewMode inspectorViewMode) =>
        this.ApplyModel(this.Model.ChangeInspectorViewMode(inspectorViewMode));

    public IReadOnlyList<DropItem> RemoveItems(IEnumerable<Guid> itemIds)
    {
        ArgumentNullException.ThrowIfNull(itemIds);

        var removals = itemIds.ToHashSet();
        var removedItems = this.Model.Items.Where(item => removals.Contains(item.Id)).ToArray();
        if (removedItems.Length == 0)
        {
            return [];
        }

        this.ReplaceModel(this.Model.RemoveItems(removals));
        return removedItems;
    }

    public bool MoveItems(IEnumerable<Guid> itemIds, int direction)
    {
        ArgumentNullException.ThrowIfNull(itemIds);
        if (direction is not (-1 or 1))
        {
            throw new ArgumentOutOfRangeException(nameof(direction));
        }

        var selected = itemIds.ToHashSet();
        if (selected.Count == 0)
        {
            return false;
        }

        var moved = false;
        this._isSynchronizingItems = true;
        try
        {
            if (direction < 0)
            {
                for (var index = 1; index < this.Items.Count; index++)
                {
                    if (selected.Contains(this.Items[index].Model.Id) &&
                        !selected.Contains(this.Items[index - 1].Model.Id))
                    {
                        this.Items.Move(index, index - 1);
                        moved = true;
                    }
                }
            }
            else
            {
                for (var index = this.Items.Count - 2; index >= 0; index--)
                {
                    if (selected.Contains(this.Items[index].Model.Id) &&
                        !selected.Contains(this.Items[index + 1].Model.Id))
                    {
                        this.Items.Move(index, index + 1);
                        moved = true;
                    }
                }
            }
        }
        finally
        {
            this._isSynchronizingItems = false;
        }

        if (moved)
        {
            this.ApplyModel(this.Model.ReorderItems(this.Items.Select(static item => item.Model.Id)));
        }

        return moved;
    }

    public bool CanMoveItems(IEnumerable<Guid> itemIds, int direction)
    {
        ArgumentNullException.ThrowIfNull(itemIds);
        if (direction is not (-1 or 1))
        {
            throw new ArgumentOutOfRangeException(nameof(direction));
        }

        var selected = itemIds.ToHashSet();
        if (direction < 0)
        {
            return Enumerable.Range(1, Math.Max(0, this.Items.Count - 1)).Any(index =>
                selected.Contains(this.Items[index].Model.Id) &&
                !selected.Contains(this.Items[index - 1].Model.Id));
        }

        return Enumerable.Range(0, Math.Max(0, this.Items.Count - 1)).Any(index =>
            selected.Contains(this.Items[index].Model.Id) &&
            !selected.Contains(this.Items[index + 1].Model.Id));
    }

    internal void ReplaceModel(DropStack model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (model.Id != this.Model.Id)
        {
            throw new ArgumentException("A stack view model cannot change identity.", nameof(model));
        }

        var existingItems = this.Items.ToDictionary(static item => item.Model.Id);
        this._isSynchronizingItems = true;
        try
        {
            if (this.Items.Select(static item => item.Model.Id).SequenceEqual(model.Items.Select(static item => item.Id)))
            {
                // Note edits must not reset every list and its selection on each keystroke.
                for (var index = 0; index < model.Items.Count; index++)
                {
                    if (!ReferenceEquals(this.Items[index].Model, model.Items[index]))
                    {
                        if (this.Items[index].Model.Note is not null && model.Items[index].Note is not null)
                        {
                            this.Items[index].UpdateNoteModel(model.Items[index]);
                        }
                        else
                        {
                            this.Items[index] = new DropItemViewModel(model.Items[index]);
                        }
                    }
                }
            }
            else
            {
                this.Items.Clear();
                foreach (var item in model.Items)
                {
                    this.Items.Add(existingItems.TryGetValue(item.Id, out var existing) && ReferenceEquals(existing.Model, item)
                        ? existing
                        : new DropItemViewModel(item));
                }
            }
        }
        finally
        {
            this._isSynchronizingItems = false;
        }

        this.ApplyModel(model);
    }

    internal void SetEdgeMembership(EdgeShelfSide? side)
    {
        if (!this.SetProperty(ref this._assignedEdge, side, nameof(this.AssignedEdge)))
        {
            return;
        }

        this.OnPropertyChanged(nameof(this.IsInEdgeWindow));
        this.OnPropertyChanged(nameof(this.CanAddToEdgeWindow));
        this.OnPropertyChanged(nameof(this.EdgePlacementText));
        this.OnPropertyChanged(nameof(this.IsNotOnEdgeShelf));
        this.OnPropertyChanged(nameof(this.IsOnLeftEdgeShelf));
        this.OnPropertyChanged(nameof(this.IsOnRightEdgeShelf));
        this.OnPropertyChanged(nameof(this.IsOnTopEdgeShelf));
        this.OnPropertyChanged(nameof(this.IsOnBottomEdgeShelf));
    }

    private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        this.SynchronizePreviewSubscriptions();
        if (!this._isSynchronizingItems && args.Action == NotifyCollectionChangedAction.Move)
        {
            this.ApplyModel(this.Model.ReorderItems(this.Items.Select(static item => item.Model.Id)));
        }
    }

    private void SynchronizePreviewSubscriptions()
    {
        foreach (var removed in this._previewItems.Where(item => !this.Items.Contains(item)).ToArray())
        {
            removed.PropertyChanged -= this.OnPreviewItemPropertyChanged;
            this._previewItems.Remove(removed);
        }

        foreach (var added in this.Items.Where(item => this._previewItems.Add(item)))
        {
            added.PropertyChanged += this.OnPreviewItemPropertyChanged;
        }

        this.NotifyPreviewChanged();
    }

    private void OnPreviewItemPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(DropItemViewModel.ThumbnailSource) or
            nameof(DropItemViewModel.ThumbnailVisibility) or
            nameof(DropItemViewModel.PlaceholderVisibility) or
            nameof(DropItemViewModel.ThumbnailBorderThickness) or
            nameof(DropItemViewModel.ThumbnailIsShellIcon) or
            nameof(DropItemViewModel.ThumbnailHasVideoFilmstrip) or
            nameof(DropItemViewModel.LeadingGlyph))
        {
            this.NotifyPreviewChanged();
        }
    }

    private void NotifyPreviewChanged()
    {
        this.OnPropertyChanged(nameof(this.LeadingGlyph));
        this.OnPropertyChanged(nameof(this.PreviewThumbnailSource));
        this.OnPropertyChanged(nameof(this.PreviewThumbnailBorderThickness));
        this.OnPropertyChanged(nameof(this.PreviewThumbnailVisibility));
        this.OnPropertyChanged(nameof(this.PreviewGlyphVisibility));
        this.OnPropertyChanged(nameof(this.PreviewThumbnailIsShellIcon));
        this.OnPropertyChanged(nameof(this.PreviewThumbnailHasVideoFilmstrip));
        this.OnPropertyChanged(nameof(this.PreviewVideoFilmstripVisibility));
        this.OnPropertyChanged(nameof(this.PreviewThumbnailStretch));
        this.OnPropertyChanged(nameof(this.SecondPreviewThumbnailSource));
        this.OnPropertyChanged(nameof(this.SecondPreviewThumbnailBorderThickness));
        this.OnPropertyChanged(nameof(this.SecondPreviewThumbnailVisibility));
        this.OnPropertyChanged(nameof(this.SecondPreviewThumbnailIsShellIcon));
        this.OnPropertyChanged(nameof(this.SecondPreviewThumbnailHasVideoFilmstrip));
        this.OnPropertyChanged(nameof(this.ThirdPreviewThumbnailSource));
        this.OnPropertyChanged(nameof(this.ThirdPreviewThumbnailBorderThickness));
        this.OnPropertyChanged(nameof(this.ThirdPreviewThumbnailVisibility));
        this.OnPropertyChanged(nameof(this.ThirdPreviewThumbnailIsShellIcon));
        this.OnPropertyChanged(nameof(this.ThirdPreviewThumbnailHasVideoFilmstrip));
    }

    private ImageSource? GetPreviewThumbnail(int index) =>
        this.GetPreviewItem(index)?.ThumbnailSource;

    private Thickness GetPreviewBorderThickness(int index) =>
        this.GetPreviewItem(index)?.ThumbnailBorderThickness ?? new Thickness(1);

    private DropItemViewModel? GetPreviewItem(int index) =>
        this.Items
            .Where(static item => item.ThumbnailSource is not null)
            .ElementAtOrDefault(index);

    internal void RefreshSystemColors()
    {
        if (!StackTintPalette.UsesSystemColor(this.Tint))
        {
            return;
        }

        this.UpdateTintBrushes();
        this.OnPropertyChanged(nameof(this.TintColor));
    }

    private void ApplyModel(DropStack model)
    {
        this.Model = model;
        this.UpdateTintBrushes();
        this.OnPropertyChanged(nameof(this.Name));
        this.OnPropertyChanged(nameof(this.CompactName));
        this.OnPropertyChanged(nameof(this.Tint));
        this.OnPropertyChanged(nameof(this.TintColor));
        this.OnPropertyChanged(nameof(this.InspectorViewMode));
        this.OnPropertyChanged(nameof(this.ItemCountText));
        this.OnPropertyChanged(nameof(this.Summary));
        this.OnPropertyChanged(nameof(this.LeadingGlyph));
        this.NotifyPreviewChanged();
        this.OnPropertyChanged(nameof(this.SecondLayerVisibility));
        this.OnPropertyChanged(nameof(this.ThirdLayerVisibility));
        this.OnPropertyChanged(nameof(this.CountBadgeVisibility));
        this.OnPropertyChanged(nameof(this.CountBadgeText));
        this.OnPropertyChanged(nameof(this.TileCountBadgeVisibility));
        this.OnPropertyChanged(nameof(this.TileCountBadgeText));
        this.OnPropertyChanged(nameof(this.AccessibleName));
        this.ModelChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateTintBrushes()
    {
        var tintColor = this.TintColor;
        this.TintBrush.Color = tintColor;
        this.TintForegroundBrush.Color = GetContrastingForeground(tintColor);
    }

    private static Color GetContrastingForeground(Color background)
    {
        var perceivedBrightness = ((background.R * 299) + (background.G * 587) + (background.B * 114)) / 1000;
        return perceivedBrightness >= 160
            ? Colors.Black
            : Colors.White;
    }

    private static Color ResolveTint(string tint) => StackTintPalette.Resolve(tint);
}

public sealed class StackCardLayoutMetrics
{
    private static readonly StackCardLayoutMetrics SmallList = new()
    {
        HeaderMinHeight = 52,
        PreviewContainerWidth = 48,
        PreviewContainerHeight = 44,
        ThirdPreviewWidth = 34,
        ThirdPreviewHeight = 30,
        SecondPreviewWidth = 38,
        SecondPreviewHeight = 33,
        FrontPreviewWidth = 42,
        FrontPreviewHeight = 36,
        ShellIconSize = 32,
        ThirdShellIconOffsetX = 0,
        ThirdShellIconOffsetY = -4,
        SecondShellIconOffsetX = -4,
        SecondShellIconOffsetY = 0,
        FrontShellIconOffsetX = 4,
        FrontShellIconOffsetY = 4,
        PreviewGlyphFontSize = 18,
        BackCornerRadius = new CornerRadius(5),
        FrontCornerRadius = new CornerRadius(6),
        ThirdTranslateY = 5,
        SecondTranslateY = 2,
        PreviewMargin = new Thickness(0, 0, 11, 0),
        PreviewColumnSpan = 1,
        PreviewHorizontalAlignment = HorizontalAlignment.Left,
        TextRow = 0,
        TextColumn = 1,
        TextColumnSpan = 1,
        TextVerticalAlignment = VerticalAlignment.Center,
        TextAlignment = Microsoft.UI.Xaml.TextAlignment.Left,
        TextMargin = new Thickness(0),
        HorizontalPanelCollapsedHeight = 122,
        HorizontalPanelExpandedHeight = 340,
        HorizontalCardWidth = 200,
        HorizontalCardHeight = 104,
        HorizontalPreviewContainerWidth = 58,
        HorizontalPreviewContainerHeight = 58,
        HorizontalThirdPreviewWidth = 42,
        HorizontalThirdPreviewHeight = 37,
        HorizontalSecondPreviewWidth = 47,
        HorizontalSecondPreviewHeight = 41,
        HorizontalFrontPreviewWidth = 52,
        HorizontalFrontPreviewHeight = 46,
        HorizontalShellIconSize = 32,
        HorizontalPreviewGlyphFontSize = 22,
        HorizontalNameWidth = 120,
        HorizontalPreviewRow = 0,
        HorizontalPreviewRowSpan = 2,
        HorizontalPreviewColumn = 0,
        HorizontalPreviewColumnSpan = 1,
        HorizontalPreviewMargin = new Thickness(0, 0, 10, 0),
        HorizontalTextRow = 0,
        HorizontalTextRowSpan = 2,
        HorizontalTextColumn = 1,
        HorizontalTextColumnSpan = 1,
        HorizontalTextVerticalAlignment = VerticalAlignment.Center,
        HorizontalTextAlignment = Microsoft.UI.Xaml.TextAlignment.Left,
        HorizontalTextMargin = new Thickness(0),
        HorizontalActionWidth = 84,
        HorizontalActionHeight = 104,
        HorizontalActionNameWidth = 76
    };

    private static readonly StackCardLayoutMetrics LargeList = new()
    {
        HeaderMinHeight = 104,
        PreviewContainerWidth = 136,
        PreviewContainerHeight = 104,
        ThirdPreviewWidth = 104,
        ThirdPreviewHeight = 68,
        SecondPreviewWidth = 112,
        SecondPreviewHeight = 74,
        FrontPreviewWidth = 120,
        FrontPreviewHeight = 80,
        ShellIconSize = 64,
        ThirdShellIconOffsetX = 0,
        ThirdShellIconOffsetY = -18,
        SecondShellIconOffsetX = -24,
        SecondShellIconOffsetY = -10,
        FrontShellIconOffsetX = 22,
        FrontShellIconOffsetY = 10,
        PreviewGlyphFontSize = 32,
        BackCornerRadius = new CornerRadius(7),
        FrontCornerRadius = new CornerRadius(8),
        ThirdTranslateY = 7,
        SecondTranslateY = 3,
        PreviewMargin = new Thickness(0, 0, 12, 0),
        PreviewColumnSpan = 1,
        PreviewHorizontalAlignment = HorizontalAlignment.Left,
        TextRow = 0,
        TextColumn = 1,
        TextColumnSpan = 1,
        TextVerticalAlignment = VerticalAlignment.Center,
        TextAlignment = Microsoft.UI.Xaml.TextAlignment.Left,
        TextMargin = new Thickness(0),
        HorizontalPanelCollapsedHeight = 122,
        HorizontalPanelExpandedHeight = 340,
        HorizontalCardWidth = 300,
        HorizontalCardHeight = 104,
        HorizontalPreviewContainerWidth = 136,
        HorizontalPreviewContainerHeight = 104,
        HorizontalThirdPreviewWidth = 104,
        HorizontalThirdPreviewHeight = 68,
        HorizontalSecondPreviewWidth = 112,
        HorizontalSecondPreviewHeight = 74,
        HorizontalFrontPreviewWidth = 120,
        HorizontalFrontPreviewHeight = 80,
        HorizontalShellIconSize = 64,
        HorizontalPreviewGlyphFontSize = 32,
        HorizontalNameWidth = 148,
        HorizontalPreviewRow = 0,
        HorizontalPreviewRowSpan = 2,
        HorizontalPreviewColumn = 0,
        HorizontalPreviewColumnSpan = 1,
        HorizontalPreviewMargin = new Thickness(0, 0, 12, 0),
        HorizontalTextRow = 0,
        HorizontalTextRowSpan = 2,
        HorizontalTextColumn = 1,
        HorizontalTextColumnSpan = 1,
        HorizontalTextVerticalAlignment = VerticalAlignment.Center,
        HorizontalTextAlignment = Microsoft.UI.Xaml.TextAlignment.Left,
        HorizontalTextMargin = new Thickness(0),
        HorizontalActionWidth = 84,
        HorizontalActionHeight = 104,
        HorizontalActionNameWidth = 76
    };

    private static readonly StackCardLayoutMetrics ThumbnailIcon = new()
    {
        HeaderMinHeight = 144,
        PreviewContainerWidth = 136,
        PreviewContainerHeight = 104,
        ThirdPreviewWidth = 104,
        ThirdPreviewHeight = 68,
        SecondPreviewWidth = 112,
        SecondPreviewHeight = 74,
        FrontPreviewWidth = 120,
        FrontPreviewHeight = 80,
        ShellIconSize = 64,
        ThirdShellIconOffsetX = 0,
        ThirdShellIconOffsetY = -18,
        SecondShellIconOffsetX = -24,
        SecondShellIconOffsetY = -10,
        FrontShellIconOffsetX = 22,
        FrontShellIconOffsetY = 10,
        PreviewGlyphFontSize = 32,
        BackCornerRadius = new CornerRadius(7),
        FrontCornerRadius = new CornerRadius(8),
        ThirdTranslateY = 7,
        SecondTranslateY = 3,
        PreviewMargin = new Thickness(0),
        PreviewColumnSpan = 3,
        PreviewHorizontalAlignment = HorizontalAlignment.Center,
        TextRow = 1,
        TextColumn = 0,
        TextColumnSpan = 3,
        TextVerticalAlignment = VerticalAlignment.Top,
        TextAlignment = Microsoft.UI.Xaml.TextAlignment.Center,
        TextMargin = new Thickness(0, 4, 0, 0),
        HorizontalPanelCollapsedHeight = 160,
        HorizontalPanelExpandedHeight = 378,
        HorizontalCardWidth = 152,
        HorizontalCardHeight = 144,
        HorizontalPreviewContainerWidth = 136,
        HorizontalPreviewContainerHeight = 104,
        HorizontalThirdPreviewWidth = 104,
        HorizontalThirdPreviewHeight = 68,
        HorizontalSecondPreviewWidth = 112,
        HorizontalSecondPreviewHeight = 74,
        HorizontalFrontPreviewWidth = 120,
        HorizontalFrontPreviewHeight = 80,
        HorizontalShellIconSize = 64,
        HorizontalPreviewGlyphFontSize = 32,
        HorizontalNameWidth = 144,
        HorizontalPreviewRow = 0,
        HorizontalPreviewRowSpan = 1,
        HorizontalPreviewColumn = 0,
        HorizontalPreviewColumnSpan = 2,
        HorizontalPreviewMargin = new Thickness(0),
        HorizontalTextRow = 1,
        HorizontalTextRowSpan = 1,
        HorizontalTextColumn = 0,
        HorizontalTextColumnSpan = 2,
        HorizontalTextVerticalAlignment = VerticalAlignment.Top,
        HorizontalTextAlignment = Microsoft.UI.Xaml.TextAlignment.Center,
        HorizontalTextMargin = new Thickness(0, 5, 0, 0),
        HorizontalActionWidth = 84,
        HorizontalActionHeight = 144,
        HorizontalActionNameWidth = 76
    };

    private StackCardLayoutMetrics()
    {
    }

    public double HeaderMinHeight { get; private init; }

    public double PreviewContainerWidth { get; private init; }

    public double PreviewContainerHeight { get; private init; }

    public double ThirdPreviewWidth { get; private init; }

    public double ThirdPreviewHeight { get; private init; }

    public double SecondPreviewWidth { get; private init; }

    public double SecondPreviewHeight { get; private init; }

    public double FrontPreviewWidth { get; private init; }

    public double FrontPreviewHeight { get; private init; }

    public double ShellIconSize { get; private init; }

    public double ThirdShellIconOffsetX { get; private init; }

    public double ThirdShellIconOffsetY { get; private init; }

    public double SecondShellIconOffsetX { get; private init; }

    public double SecondShellIconOffsetY { get; private init; }

    public double FrontShellIconOffsetX { get; private init; }

    public double FrontShellIconOffsetY { get; private init; }

    public double PreviewGlyphFontSize { get; private init; }

    public CornerRadius BackCornerRadius { get; private init; }

    public CornerRadius FrontCornerRadius { get; private init; }

    public double ThirdTranslateY { get; private init; }

    public double SecondTranslateY { get; private init; }

    public Thickness PreviewMargin { get; private init; }

    public int PreviewColumnSpan { get; private init; }

    public HorizontalAlignment PreviewHorizontalAlignment { get; private init; }

    public int TextRow { get; private init; }

    public int TextColumn { get; private init; }

    public int TextColumnSpan { get; private init; }

    public VerticalAlignment TextVerticalAlignment { get; private init; }

    public TextAlignment TextAlignment { get; private init; }

    public Thickness TextMargin { get; private init; }

    public double HorizontalPanelCollapsedHeight { get; private init; }

    public double HorizontalPanelExpandedHeight { get; private init; }

    public double HorizontalCardWidth { get; private init; }

    public double HorizontalCardHeight { get; private init; }

    public double HorizontalPreviewContainerWidth { get; private init; }

    public double HorizontalPreviewContainerHeight { get; private init; }

    public double HorizontalThirdPreviewWidth { get; private init; }

    public double HorizontalThirdPreviewHeight { get; private init; }

    public double HorizontalSecondPreviewWidth { get; private init; }

    public double HorizontalSecondPreviewHeight { get; private init; }

    public double HorizontalFrontPreviewWidth { get; private init; }

    public double HorizontalFrontPreviewHeight { get; private init; }

    public double HorizontalShellIconSize { get; private init; }

    public double HorizontalPreviewGlyphFontSize { get; private init; }

    public double HorizontalNameWidth { get; private init; }

    public int HorizontalPreviewRow { get; private init; }

    public int HorizontalPreviewRowSpan { get; private init; }

    public int HorizontalPreviewColumn { get; private init; }

    public int HorizontalPreviewColumnSpan { get; private init; }

    public Thickness HorizontalPreviewMargin { get; private init; }

    public int HorizontalTextRow { get; private init; }

    public int HorizontalTextRowSpan { get; private init; }

    public int HorizontalTextColumn { get; private init; }

    public int HorizontalTextColumnSpan { get; private init; }

    public VerticalAlignment HorizontalTextVerticalAlignment { get; private init; }

    public TextAlignment HorizontalTextAlignment { get; private init; }

    public Thickness HorizontalTextMargin { get; private init; }

    public double HorizontalActionWidth { get; private init; }

    public double HorizontalActionHeight { get; private init; }

    public double HorizontalActionNameWidth { get; private init; }

    public static StackCardLayoutMetrics Resolve(StackCardDisplayMode displayMode) => displayMode switch
    {
        StackCardDisplayMode.SmallList => SmallList,
        StackCardDisplayMode.ThumbnailIcon => ThumbnailIcon,
        _ => LargeList
    };
}

public sealed class DropItemViewModel : ObservableObject
{
    private string _leadingGlyph = "\uE7B8";
    private ImageSource? _thumbnailSource;
    private string _thumbnailAccessibleLabel = "Content";
    private ContentThumbnailChrome _thumbnailChrome;
    private string _thumbnailProviderId = string.Empty;

    public DropItemViewModel(DropItem model)
    {
        this.Model = model;
        var fallback = ContentThumbnailFallback.For(model.Kind);
        this._leadingGlyph = fallback.Glyph!;
        this._thumbnailAccessibleLabel = fallback.AccessibleLabel;
        _ = this.LoadThumbnailAsync();
    }

    public DropItem Model { get; private set; }

    internal void UpdateNoteModel(DropItem model)
    {
        if (model.Id != this.Model.Id || model.Note is null || this.Model.Note is null)
        {
            throw new ArgumentException("Only an existing note item can be updated in place.", nameof(model));
        }
        this.Model = model;
        this.OnPropertyChanged(nameof(this.Model));
        this.OnPropertyChanged(nameof(this.DisplayName));
        this.OnPropertyChanged(nameof(this.KindLabel));
        this.OnPropertyChanged(nameof(this.AccessibleName));
    }

    public Visibility NoteVisibility => this.Model.Note is null ? Visibility.Collapsed : Visibility.Visible;

    public Visibility NonNoteVisibility => this.Model.Note is null ? Visibility.Visible : Visibility.Collapsed;

    public string DisplayName => this.Model.DisplayName;

    public string KindLabel
    {
        get
        {
            var metadata = ContentMetadataPolicy.GetMetadata(this.Model);
            var representations = new List<string>
            {
                this.Model.Kind == DropItemKind.Uri ? "URL" : this.Model.Kind.ToString()
            };
            foreach (var tag in metadata.Tags)
            {
                if (!representations.Contains(tag.DisplayName, StringComparer.OrdinalIgnoreCase))
                {
                    representations.Add(tag.DisplayName);
                }
            }

            if (metadata.Representations.HasFlag(ContentRepresentations.Html))
            {
                representations.Add("HTML");
            }

            if (metadata.Representations.HasFlag(ContentRepresentations.Rtf))
            {
                representations.Add("RTF");
            }

            if (metadata.Representations.HasFlag(ContentRepresentations.ApplicationLink))
            {
                representations.Add("App link");
            }

            if (metadata.Representations.HasFlag(ContentRepresentations.Custom))
            {
                representations.Add($"Native ×{this.Model.CustomFormats.Count}");
            }

            if (ContentDetection.TryNormalizeWebUrl(this.Model.SourceUrl, out var sourceUrl) &&
                Uri.TryCreate(sourceUrl, UriKind.Absolute, out var sourceUri))
            {
                representations.Add(sourceUri.Host);
            }
            else if (!string.IsNullOrWhiteSpace(this.Model.SourceApplicationName))
            {
                representations.Add(this.Model.SourceApplicationName);
            }

            return string.Join(" · ", representations);
        }
    }

    public string AccessibleName => $"{this.DisplayName}, {this.KindLabel}";

    public string LeadingGlyph
    {
        get => this._leadingGlyph;
        private set => this.SetProperty(ref this._leadingGlyph, value);
    }

    public string ThumbnailAccessibleLabel
    {
        get => this._thumbnailAccessibleLabel;
        private set => this.SetProperty(ref this._thumbnailAccessibleLabel, value);
    }

    public ContentThumbnailChrome ThumbnailChrome
    {
        get => this._thumbnailChrome;
        private set
        {
            if (this.SetProperty(ref this._thumbnailChrome, value))
            {
                this.OnPropertyChanged(nameof(this.ThumbnailBorderThickness));
                this.OnPropertyChanged(nameof(this.ThumbnailIsShellIcon));
                this.OnPropertyChanged(nameof(this.ThumbnailHasVideoFilmstrip));
                this.OnPropertyChanged(nameof(this.VideoFilmstripVisibility));
            }
        }
    }

    public Thickness ThumbnailBorderThickness =>
        this.ThumbnailChrome == ContentThumbnailChrome.None
            ? new Thickness(0)
            : new Thickness(1);

    public string ThumbnailProviderId
    {
        get => this._thumbnailProviderId;
        private set
        {
            if (this.SetProperty(ref this._thumbnailProviderId, value))
            {
                this.OnPropertyChanged(nameof(this.ThumbnailIsShellIcon));
                this.OnPropertyChanged(nameof(this.ThumbnailHasVideoFilmstrip));
                this.OnPropertyChanged(nameof(this.VideoFilmstripVisibility));
            }
        }
    }

    public ImageSource? ThumbnailSource
    {
        get => this._thumbnailSource;
        private set
        {
            if (this.SetProperty(ref this._thumbnailSource, value))
            {
                this.OnPropertyChanged(nameof(this.ThumbnailVisibility));
                this.OnPropertyChanged(nameof(this.PlaceholderVisibility));
                this.OnPropertyChanged(nameof(this.ThumbnailIsShellIcon));
                this.OnPropertyChanged(nameof(this.ThumbnailHasVideoFilmstrip));
                this.OnPropertyChanged(nameof(this.VideoFilmstripVisibility));
            }
        }
    }

    public Visibility ThumbnailVisibility =>
        this.ThumbnailSource is null
            ? Visibility.Collapsed
            : Visibility.Visible;

    public Visibility PlaceholderVisibility =>
        this.Model.Note is null && this.ThumbnailSource is null
            ? Visibility.Visible
            : Visibility.Collapsed;

    public bool ThumbnailIsShellIcon =>
        this.ThumbnailSource is not null &&
        this.ThumbnailChrome == ContentThumbnailChrome.None &&
        string.Equals(
            this.ThumbnailProviderId,
            "omnitray.shell-thumbnail",
            StringComparison.Ordinal);

    public bool ThumbnailHasVideoFilmstrip =>
        this.ThumbnailSource is not null &&
        !this.ThumbnailIsShellIcon &&
        string.Equals(
            this.ThumbnailProviderId,
            "omnitray.shell-thumbnail",
            StringComparison.Ordinal) &&
        ContentDetection.IsVideoFile(
            this.Model.FileFacts?.ContentType,
            Path.GetExtension(this.Model.SourcePath));

    public Visibility VideoFilmstripVisibility =>
        this.ThumbnailHasVideoFilmstrip ? Visibility.Visible : Visibility.Collapsed;

    private async Task LoadThumbnailAsync()
    {
        try
        {
            var presentation = await ContentThumbnailService.Default.ResolveAsync(this.Model);
            this.LeadingGlyph = presentation.Glyph;
            this.ThumbnailAccessibleLabel = presentation.AccessibleLabel;
            this.ThumbnailChrome = presentation.Chrome;
            this.ThumbnailProviderId = presentation.ProviderId;
            this.ThumbnailSource = presentation.ImageSource;
        }
        catch
        {
            // Provider and source failures retain the stable generic fallback.
        }
    }
}
