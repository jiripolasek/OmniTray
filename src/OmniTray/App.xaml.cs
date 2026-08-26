// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.ComponentModel;
using System.Diagnostics;
using Windows.ApplicationModel.Activation;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI.ViewManagement;
using H.NotifyIcon;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Windows.AppLifecycle;
using WinRT.Interop;
using LaunchActivatedEventArgs = Microsoft.UI.Xaml.LaunchActivatedEventArgs;

namespace OmniTray;

public partial class App : Application
{
    private readonly object _activationSync = new();
    private readonly AppSettingsService _appSettingsService;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly DropCommandExecutionService _dropCommandExecutionService = new();
    private readonly DropCommandRepository _dropCommandRepository = new();
    private readonly Dictionary<EdgeShelfSide, MenuFlyoutItem> _edgeShelfMenuItems = [];
    private readonly Queue<AppActivationArguments> _pendingActivations = new();
    private readonly HashSet<Guid> _runningDropCommands = [];
    private readonly StackRepository _stackRepository = new();
    private readonly UISettings _systemUiSettings = new();
    private CancellationTokenSource? _catalogSaveDebounce;
    private CancellationTokenSource? _dropCommandSaveDebounce;
    private MenuFlyoutSubItem? _edgeShelfMenu;
    private ToggleMenuFlyoutItem? _gameModeMenuItem;
    private bool _isInitialized;
    private ToggleMenuFlyoutItem? _pauseEdgeWindowsMenuItem;
    private TaskbarIcon? _trayIcon;
    private WindowCoordinator? _windows;

    public App()
    {
        this.InitializeComponent();
        this._appSettingsService = new AppSettingsService();
        StackTintPalette.UseSystemAccentForNeutral = this._appSettingsService.UseSystemAccentForNeutral;
        this._dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        this.StackCatalogViewModel.EdgeWindowsPaused = this._appSettingsService.EdgeWindowsPaused;
        this.StackCatalogViewModel.GameModeEnabled = this._appSettingsService.EdgeGameModeEnabled;
        this.StackCatalogViewModel.LeftEdgeWindowEnabled = this._appSettingsService.LeftEdgeWindowEnabled;
        this.StackCatalogViewModel.RightEdgeWindowEnabled = this._appSettingsService.RightEdgeWindowEnabled;
        this.StackCatalogViewModel.TopEdgeWindowEnabled = this._appSettingsService.TopEdgeWindowEnabled;
        this.StackCatalogViewModel.BottomEdgeWindowEnabled = this._appSettingsService.BottomEdgeWindowEnabled;
        this.StackCatalogViewModel.VerticalStackCardDisplayMode =
            this._appSettingsService.VerticalStackCardDisplayMode;
        this.StackCatalogViewModel.HorizontalStackCardDisplayMode =
            this._appSettingsService.HorizontalStackCardDisplayMode;
        foreach (var side in Enum.GetValues<EdgeShelfSide>())
        {
            this.StackCatalogViewModel.SetEdgeWindowSizeMode(
                side,
                this._appSettingsService.GetEdgeWindowSizeMode(side));
            this.StackCatalogViewModel.SetEdgeWindowAlignment(
                side,
                this._appSettingsService.GetEdgeWindowAlignment(side));
        }

        this.StackCatalogViewModel.SyncLeftAndRightEdgeContent
            = this._appSettingsService.SyncLeftAndRightEdgeContent;
        this.StackCatalogViewModel.SyncTopAndBottomEdgeContent
            = this._appSettingsService.SyncTopAndBottomEdgeContent;
        this.StackCatalogViewModel.SyncAllEdgeContent = this._appSettingsService.SyncAllEdgeContent;
        this.StackCatalogViewModel.PropertyChanged += this.OnStackCatalogPropertyChanged;
        this.StackCatalogViewModel.CatalogChanged += (_, _) => this.QueueCatalogSave();
        this.StackCatalogViewModel.Stacks.CollectionChanged += (_, _) => this.RunOnUiThread(() =>
            this._windows?.ReconcileTrays(this.StackCatalogViewModel.Stacks.Select(static stack => stack.Model.Id)
                .ToHashSet()));
        this.DropCommandCatalogViewModel.CatalogChanged += (_, _) =>
        {
            this.QueueDropCommandSave();
            this.RunOnUiThread(() => this._windows?.ReconcileDropCommandWindows(
                this.DropCommandCatalogViewModel.Commands.Select(static command => command.Id).ToHashSet()));
        };
        this._systemUiSettings.ColorValuesChanged += this.OnSystemColorValuesChanged;
    }

    public static new App Current => (App)Application.Current;

    public MainViewModel StackCatalogViewModel { get; } = new();

    internal DropCommandCatalogViewModel DropCommandCatalogViewModel { get; } = new();

    internal bool AllowMoveOnDragOutPreference
    {
        get => this._appSettingsService.AllowMoveOnDragOut;
        set => this._appSettingsService.AllowMoveOnDragOut = value;
    }

    internal bool OpenInspectorOnHoverPreference
    {
        get => this._appSettingsService.OpenInspectorOnHover;
        set => this._appSettingsService.OpenInspectorOnHover = value;
    }

    internal bool ShakeToCreateTrayPreference
    {
        get => this._appSettingsService.ShakeToCreateTray;
        set => this._appSettingsService.ShakeToCreateTray = value;
    }

    internal bool UseSystemAccentForNeutralPreference
    {
        get => this._appSettingsService.UseSystemAccentForNeutral;
        set
        {
            if (this._appSettingsService.UseSystemAccentForNeutral == value)
            {
                return;
            }

            this._appSettingsService.UseSystemAccentForNeutral = value;
            StackTintPalette.UseSystemAccentForNeutral = value;
            this.StackCatalogViewModel.RefreshSystemColors();
            this.DropCommandCatalogViewModel.RefreshSystemColors();
        }
    }

    internal ToastPosition ToastPositionPreference
    {
        get => this._appSettingsService.ToastPosition;
        set => this._appSettingsService.ToastPosition = value;
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        var catalogTask = this._stackRepository.LoadAsync();
        var commandCatalogTask = this._dropCommandRepository.LoadAsync();
        await Task.WhenAll(catalogTask, commandCatalogTask);
        var catalog = await catalogTask;
        var commandCatalog = await commandCatalogTask;
        this.StackCatalogViewModel.RestoreStacks(catalog.Stacks);
        this.StackCatalogViewModel.RestoreEdgeShelves(catalog.EdgeShelves);
        this.DropCommandCatalogViewModel.Restore(commandCatalog);
        this._windows = new WindowCoordinator(
            this.StackCatalogViewModel,
            this.DropCommandCatalogViewModel,
            this._dispatcherQueue,
            () => this._appSettingsService.ShakeToCreateTray);
        this._windows.TrayWindowStatesChanged += (_, _) => this.QueueCatalogSave();
        this._windows.DropCommandWindowStatesChanged += (_, _) => this.QueueDropCommandSave();
        this._windows.RestoreTrays(
            catalog.OpenTrayWindows,
            stackId => this.StackCatalogViewModel.Stacks.FirstOrDefault(stack => stack.Model.Id == stackId));
        this._windows.RestoreDropCommandWindows(
            commandCatalog.OpenWindows,
            commandId => this.DropCommandCatalogViewModel.FindCommand(commandId));
        this.InitializeTrayIcon();
        this.CompleteInitialization();
    }

    public void ShowSettings() => this.RunOnUiThread(() => this._windows?.ShowSettings());

    public void ShowPopup() => this.RunOnUiThread(() => this._windows?.ShowPopup());

    public void HidePopup() => this.RunOnUiThread(() => this._windows?.HidePopup());

    public void ShowEdgeShelf(EdgeShelfSide side = EdgeShelfSide.Right) =>
        this.RunOnUiThread(() => this._windows?.ShowEdgeShelf(side));

    public void HideAllEdgeShelves() => this.RunOnUiThread(() => this._windows?.HideAllEdgeShelves());

    public void OpenTray(DropStackViewModel stack) => this.RunOnUiThread(() => this._windows?.ShowTray(stack));

    internal void OpenDropCommand(DropCommandViewModel command) =>
        this.RunOnUiThread(() => this._windows?.ShowDropCommand(command));

    internal bool CanPotentiallyExecuteDropCommand(Guid commandId, DataPackageView dataView)
    {
        var command = this.DropCommandCatalogViewModel.FindCommand(commandId);
        return command is not null &&
               !this._runningDropCommands.Contains(commandId) &&
               this._dropCommandExecutionService.CanPotentiallyExecute(
                   command.Model,
                   dataView,
                   this.StackCatalogViewModel);
    }

    internal bool CanPotentiallyExecuteDropCommandFolder(
        string surfaceId,
        Guid folderId,
        DataPackageView dataView) =>
        this.DropCommandCatalogViewModel.GetDescendantCommandIds(surfaceId, folderId)
            .Any(commandId => this.CanPotentiallyExecuteDropCommand(commandId, dataView));

    internal async Task ExecuteDropCommandAsync(
        Guid commandId,
        DataPackageView dataView,
        Window owner)
    {
        ArgumentNullException.ThrowIfNull(dataView);
        var input = await DropCommandInputResolver.ResolveAsync(dataView, this.StackCatalogViewModel);
        await this.ExecuteDropCommandCoreAsync(commandId, input, owner);
    }

    internal Task ExecuteDropCommandAsync(Guid commandId, DropStackViewModel stack, Window owner) =>
        this.ExecuteDropCommandCoreAsync(
            commandId,
            DropCommandInputResolver.FromStack(stack),
            owner);

    internal void HandleActivation(AppActivationArguments args)
    {
        ArgumentNullException.ThrowIfNull(args);
        lock (this._activationSync)
        {
            if (!this._isInitialized)
            {
                this._pendingActivations.Enqueue(args);
                return;
            }
        }

        this.RunOnUiThread(() => this.HandleActivationCore(args));
    }

    internal void ShowToast(string message, InfoBarSeverity severity) =>
        this.RunOnUiThread(() => this._windows?.ShowToast(
            message,
            severity, this._appSettingsService.ToastPosition));

    private async Task ExecuteDropCommandCoreAsync(
        Guid commandId,
        DropCommandInput input,
        Window owner)
    {
        var command = this.DropCommandCatalogViewModel.FindCommand(commandId);
        if (command is null || !this._runningDropCommands.Add(commandId))
        {
            return;
        }

        var commandOwnsTransientItemLifetime = false;
        try
        {
            if (!this._dropCommandExecutionService.CanExecute(command.Model, input, out var reason))
            {
                this.ShowToast(reason, InfoBarSeverity.Warning);
                return;
            }

            var confirmationContext = new DropCommandConfirmationContext(
                input.Items.Count,
                input.SourceReference is not null);
            if (DropCommandTemplates.CreateConfirmation(command.Model, confirmationContext) is { } confirmation &&
                !await DropCommandDialogService.ConfirmExecutionAsync(owner, confirmation))
            {
                return;
            }

            var result = await this._dropCommandExecutionService.ExecuteAsync(
                command.Model,
                input,
                WindowNative.GetWindowHandle(owner));
            commandOwnsTransientItemLifetime = result.OwnsTransientItemLifetime;
            if (result.ConsumeSuccessfulSourceItems &&
                result.SuccessfulItemIds.Count > 0 &&
                input.SourceReference is { } source &&
                this.StackCatalogViewModel.Stacks.FirstOrDefault(stack => stack.Model.Id == source.StackId) is
                { } sourceStack)
            {
                var sourceIds = source.ItemIds.ToHashSet();
                await this.RemoveItemsAsync(
                    sourceStack,
                    result.SuccessfulItemIds.Where(sourceIds.Contains));
            }

            if (result.IsSuccess)
            {
                if (!result.ReportsProgressExternally)
                {
                    this.ShowToast(
                        $"{command.Name} completed for {result.SucceededCount} {(result.SucceededCount == 1 ? "item" : "items")}.",
                        InfoBarSeverity.Success);
                }
            }
            else if (result.IsPartial)
            {
                this.ShowToast(
                    $"{command.Name} completed for {result.SucceededCount} items; {result.FailedCount} failed.",
                    InfoBarSeverity.Warning);
            }
            else
            {
                this.ShowToast(
                    string.IsNullOrWhiteSpace(result.ErrorMessage)
                        ? $"{command.Name} could not be completed."
                        : result.ErrorMessage,
                    InfoBarSeverity.Error);
            }
        }
        finally
        {
            this._runningDropCommands.Remove(commandId);
            if (!commandOwnsTransientItemLifetime)
            {
                await ContentStore.DeleteOwnedAsync(
                    input.Items.Where(static item => item.IsTransient).Select(static item => item.Item));
            }
        }
    }

    public async Task DeleteStackAsync(DropStackViewModel stack)
    {
        ArgumentNullException.ThrowIfNull(stack);

        var ownedItems = stack.Model.Items.ToArray();
        if (this.StackCatalogViewModel.RemoveStack(stack))
        {
            await ContentStore.DeleteOwnedAsync(ownedItems);
        }
    }

    public async Task ClearStacksAsync()
    {
        var ownedItems = this.StackCatalogViewModel.Stacks
            .SelectMany(static stack => stack.Model.Items)
            .ToArray();
        this.StackCatalogViewModel.ClearStacks();
        await ContentStore.DeleteOwnedAsync(ownedItems);
    }

    public int SweepEmptyStacks() => this.StackCatalogViewModel.RemoveEmptyStacks();

    public async Task RemoveItemsAsync(DropStackViewModel stack, IEnumerable<Guid> itemIds)
    {
        ArgumentNullException.ThrowIfNull(stack);
        ArgumentNullException.ThrowIfNull(itemIds);

        var removedItems = stack.RemoveItems(itemIds);
        await ContentStore.DeleteOwnedAsync(removedItems);
    }

    internal async Task InsertClipboardContentAsync(DropStackViewModel stack)
    {
        ArgumentNullException.ThrowIfNull(stack);

        try
        {
            var items = await DragDropDataService.ReadAsync(Clipboard.GetContent());
            if (items.Count == 0)
            {
                this.ShowToast(
                    "The clipboard does not contain files, folders, text, or an image.",
                    InfoBarSeverity.Warning);
                return;
            }

            if (!this.StackCatalogViewModel.Stacks.Contains(stack))
            {
                this.ShowToast("That stack is no longer available.", InfoBarSeverity.Warning);
                return;
            }

            var addedCount = stack.AppendDroppedItems(items);
            var skippedCount = items.Count - addedCount;
            if (addedCount == 0)
            {
                this.ShowToast(
                    $"No items were added to {stack.Name}; the filesystem items are already in this stack.",
                    InfoBarSeverity.Informational);
                return;
            }

            var message = skippedCount == 0
                ? addedCount == 1
                    ? $"Added 1 item to {stack.Name}."
                    : $"Added {addedCount} items to {stack.Name}."
                : $"Added {addedCount} {(addedCount == 1 ? "item" : "items")} to {stack.Name} and skipped " +
                  $"{skippedCount} already-present filesystem {(skippedCount == 1 ? "item" : "items")}.";
            this.ShowToast(message, InfoBarSeverity.Success);
        }
        catch (Exception exception)
        {
            this.ShowToast(
                $"The clipboard content could not be captured: {exception.Message}",
                InfoBarSeverity.Error);
        }
    }

    internal async Task CompleteStackDragAsync(DataPackageOperation dropResult)
    {
        var stackId = DragDropDataService.CompleteStackDrag(dropResult);
        if (stackId is not { } id)
        {
            return;
        }

        var stack = this.StackCatalogViewModel.Stacks.FirstOrDefault(candidate => candidate.Model.Id == id);
        if (stack is null)
        {
            return;
        }

        var stackName = stack.Name;
        await this.DeleteStackAsync(stack);
        this.ShowToast($"Moved {stackName} out of OmniTray.", InfoBarSeverity.Success);
    }

    internal async Task CompleteItemDragAsync(DataPackageOperation dropResult)
    {
        var itemReference = DragDropDataService.CompleteItemDrag(dropResult);
        if (itemReference is null)
        {
            return;
        }

        var stack = this.StackCatalogViewModel.Stacks.FirstOrDefault(candidate =>
            candidate.Model.Id == itemReference.SourceStackId);
        if (stack is null)
        {
            return;
        }

        var requestedIds = itemReference.ItemIds.ToHashSet();
        var removalIds = stack.Model.Items
            .Where(item => requestedIds.Contains(item.Id))
            .Select(static item => item.Id)
            .ToArray();
        if (removalIds.Length == 0)
        {
            return;
        }

        await this.RemoveItemsAsync(stack, removalIds);
        this.ShowToast(
            removalIds.Length == 1
                ? "Moved 1 item out of OmniTray."
                : $"Moved {removalIds.Length} items out of OmniTray.",
            InfoBarSeverity.Success);
    }

    public DropStackViewModel SplitStack(
        DropStackViewModel stack,
        IEnumerable<Guid> itemIds) =>
        this.StackCatalogViewModel.SplitStack(stack, itemIds);

    public bool CombineStacks(
        DropStackViewModel target,
        DropStackViewModel source) =>
        this.StackCatalogViewModel.CombineStacks(target, source);

    internal async Task<bool> TransferItemsAsync(
        ItemDragReference itemReference,
        DropStackViewModel target,
        int targetIndex,
        bool copy,
        bool allowSameStackCopy = false)
    {
        ArgumentNullException.ThrowIfNull(itemReference);
        ArgumentNullException.ThrowIfNull(target);

        var source = this.StackCatalogViewModel.Stacks.FirstOrDefault(stack =>
            stack.Model.Id == itemReference.SourceStackId);
        if (source is null || !this.StackCatalogViewModel.Stacks.Contains(target))
        {
            return false;
        }

        var requestedIds = itemReference.ItemIds.ToHashSet();
        var sourceItems = source.Model.Items
            .Where(item => requestedIds.Contains(item.Id))
            .ToArray();
        if (sourceItems.Length != requestedIds.Count)
        {
            return false;
        }

        if (ReferenceEquals(source, target) && !allowSameStackCopy)
        {
            copy = false;
        }

        if (!copy)
        {
            return this.StackCatalogViewModel.MoveItems(
                source,
                target,
                itemReference.ItemIds,
                targetIndex);
        }

        var copies = await ContentStore.CopyItemsAsync(sourceItems);
        if (!this.StackCatalogViewModel.Stacks.Contains(source) ||
            !this.StackCatalogViewModel.Stacks.Contains(target) ||
            itemReference.ItemIds.Any(id => source.Model.Items.All(item => item.Id != id)))
        {
            await ContentStore.DeleteOwnedAsync(copies);
            return false;
        }

        if (this.StackCatalogViewModel.InsertItems(target, copies, targetIndex))
        {
            return true;
        }

        await ContentStore.DeleteOwnedAsync(copies);
        return false;
    }

    private void InitializeTrayIcon()
    {
        var showPopupCommand = this.CreateUiCommand(() => this._windows?.TogglePopup());
        var createStackCommand = this.CreateUiCommand(this.CreateAndOpenStack);
        var showSettingsCommand = this.CreateUiCommand(() => this._windows?.ShowSettings());
        var exitCommand = this.CreateUiCommand(this.ExitApplication);

        var contextMenu = new MenuFlyout { AreOpenCloseAnimationsEnabled = false };
        contextMenu.Items.Add(new MenuFlyoutItem
        {
            Text = "Open OmniTray", Icon = new FontIcon { Glyph = "\uE7B8" }, Command = showPopupCommand
        });
        contextMenu.Items.Add(new MenuFlyoutItem
        {
            Text = "New stack", Icon = new FontIcon { Glyph = "\uE710" }, Command = createStackCommand
        });
        this._edgeShelfMenu = new MenuFlyoutSubItem
        {
            Text = "Open edge shelf", Icon = new FontIcon { Glyph = "\uE90C" }
        };
        foreach (var side in Enum.GetValues<EdgeShelfSide>())
        {
            var edgeMenuItem = new MenuFlyoutItem
            {
                Text = side.GetDisplayName(),
                Command = this.CreateUiCommand(() => this._windows?.ShowEdgeShelf(side))
            };
            this._edgeShelfMenuItems.Add(side, edgeMenuItem);
            this._edgeShelfMenu.Items.Add(edgeMenuItem);
        }

        contextMenu.Items.Add(this._edgeShelfMenu);
        this._pauseEdgeWindowsMenuItem = new ToggleMenuFlyoutItem
        {
            Text = "Pause edge windows", IsChecked = this.StackCatalogViewModel.EdgeWindowsPaused
        };
        this._pauseEdgeWindowsMenuItem.Click += (_, _) =>
            this.StackCatalogViewModel.EdgeWindowsPaused = this._pauseEdgeWindowsMenuItem.IsChecked;
        contextMenu.Items.Add(this._pauseEdgeWindowsMenuItem);
        this._gameModeMenuItem = new ToggleMenuFlyoutItem
        {
            Text = "Game mode", IsChecked = this.StackCatalogViewModel.GameModeEnabled
        };
        this._gameModeMenuItem.Click += (_, _) =>
            this.StackCatalogViewModel.GameModeEnabled = this._gameModeMenuItem.IsChecked;
        contextMenu.Items.Add(this._gameModeMenuItem);
        this.UpdateEdgeMenuState();
        contextMenu.Items.Add(new MenuFlyoutItem
        {
            Text = "Settings", Icon = new FontIcon { Glyph = "\uE713" }, Command = showSettingsCommand
        });
        contextMenu.Items.Add(new MenuFlyoutSeparator());
        contextMenu.Items.Add(new MenuFlyoutItem { Text = "Exit OmniTray", Command = exitCommand });

        this._trayIcon = new TaskbarIcon
        {
            ToolTipText = "OmniTray",
            NoLeftClickDelay = false,
            LeftClickCommand = showPopupCommand,
            DoubleClickCommand = createStackCommand,
            ContextMenuMode = ContextMenuMode.PopupMenu,
            ContextFlyout = contextMenu,
            IconSource = new BitmapImage(new Uri("ms-appx:///Assets/OmniTray.ico"))
        };

        this._trayIcon.ForceCreate(false);
    }

    private RelayCommand CreateUiCommand(Action action) => new(() => this.RunOnUiThread(action));

    private void CreateAndOpenStack()
    {
        var newStack = this.StackCatalogViewModel.AddStack(DropStack.CreateEmpty());
        this._windows?.ShowTray(newStack);
    }

    private void CompleteInitialization()
    {
        AppActivationArguments[] pendingActivations;
        lock (this._activationSync)
        {
            this._isInitialized = true;
            pendingActivations = this._pendingActivations.ToArray();
            this._pendingActivations.Clear();
        }

        this.HandleActivationCore(Program.InitialActivationArguments);
        foreach (var activation in pendingActivations)
        {
            this.HandleActivationCore(activation);
        }
    }

    private void HandleActivationCore(AppActivationArguments args)
    {
        if (args.Kind == ExtendedActivationKind.StartupTask)
        {
            return;
        }

        if (args.Kind != ExtendedActivationKind.Protocol ||
            args.Data is not IProtocolActivatedEventArgs protocolArgs ||
            !OmniTrayActivation.TryParse(protocolArgs.Uri, out var request) ||
            request is null)
        {
            this._windows?.ShowPopup();
            return;
        }

        switch (request.Kind)
        {
            case OmniTrayActivationKind.Open:
                this._windows?.ShowPopup();
                break;

            case OmniTrayActivationKind.Settings:
                this._windows?.ShowSettings();
                break;

            case OmniTrayActivationKind.NewStack:
                this.CreateAndOpenStack();
                break;

            case OmniTrayActivationKind.Stack:
                var stack = this.StackCatalogViewModel.Stacks.FirstOrDefault(candidate =>
                    candidate.Model.Id == request.StackId);
                if (stack is not null)
                {
                    this._windows?.ShowTray(stack);
                }
                else
                {
                    this._windows?.ShowPopup();
                    this.ShowToast("That stack is no longer available.", InfoBarSeverity.Warning);
                }

                break;

            case OmniTrayActivationKind.EdgeShelf when
                Enum.TryParse<EdgeShelfSide>(request.Edge, true, out var side) &&
                Enum.IsDefined(side):
                this._windows?.ShowEdgeShelf(side);
                break;

            default:
                this._windows?.ShowPopup();
                break;
        }
    }

    private void OnStackCatalogPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        switch (args.PropertyName)
        {
            case nameof(MainViewModel.EdgeWindowsPaused):
                this._appSettingsService.EdgeWindowsPaused = this.StackCatalogViewModel.EdgeWindowsPaused;
                if (this._pauseEdgeWindowsMenuItem is not null)
                {
                    this._pauseEdgeWindowsMenuItem.IsChecked = this.StackCatalogViewModel.EdgeWindowsPaused;
                }

                this.UpdateEdgeMenuState();
                break;

            case nameof(MainViewModel.GameModeEnabled):
                this._appSettingsService.EdgeGameModeEnabled = this.StackCatalogViewModel.GameModeEnabled;
                if (this._gameModeMenuItem is not null)
                {
                    this._gameModeMenuItem.IsChecked = this.StackCatalogViewModel.GameModeEnabled;
                }

                break;

            case nameof(MainViewModel.IsGameModeSuppressing):
                this.UpdateEdgeMenuState();
                break;

            case nameof(MainViewModel.LeftEdgeWindowEnabled):
                this._appSettingsService.LeftEdgeWindowEnabled = this.StackCatalogViewModel.LeftEdgeWindowEnabled;
                this.UpdateEdgeMenuState();
                break;

            case nameof(MainViewModel.RightEdgeWindowEnabled):
                this._appSettingsService.RightEdgeWindowEnabled = this.StackCatalogViewModel.RightEdgeWindowEnabled;
                this.UpdateEdgeMenuState();
                break;

            case nameof(MainViewModel.TopEdgeWindowEnabled):
                this._appSettingsService.TopEdgeWindowEnabled = this.StackCatalogViewModel.TopEdgeWindowEnabled;
                this.UpdateEdgeMenuState();
                break;

            case nameof(MainViewModel.BottomEdgeWindowEnabled):
                this._appSettingsService.BottomEdgeWindowEnabled = this.StackCatalogViewModel.BottomEdgeWindowEnabled;
                this.UpdateEdgeMenuState();
                break;

            case nameof(MainViewModel.VerticalStackCardDisplayMode):
                this._appSettingsService.VerticalStackCardDisplayMode =
                    this.StackCatalogViewModel.VerticalStackCardDisplayMode;
                break;

            case nameof(MainViewModel.HorizontalStackCardDisplayMode):
                this._appSettingsService.HorizontalStackCardDisplayMode =
                    this.StackCatalogViewModel.HorizontalStackCardDisplayMode;
                break;

            case nameof(MainViewModel.LeftEdgeWindowSizeMode):
            case nameof(MainViewModel.LeftEdgeWindowAlignment):
                this.SaveEdgeWindowPresentation(EdgeShelfSide.Left);
                break;

            case nameof(MainViewModel.RightEdgeWindowSizeMode):
            case nameof(MainViewModel.RightEdgeWindowAlignment):
                this.SaveEdgeWindowPresentation(EdgeShelfSide.Right);
                break;

            case nameof(MainViewModel.TopEdgeWindowSizeMode):
            case nameof(MainViewModel.TopEdgeWindowAlignment):
                this.SaveEdgeWindowPresentation(EdgeShelfSide.Top);
                break;

            case nameof(MainViewModel.BottomEdgeWindowSizeMode):
            case nameof(MainViewModel.BottomEdgeWindowAlignment):
                this.SaveEdgeWindowPresentation(EdgeShelfSide.Bottom);
                break;

            case nameof(MainViewModel.SyncLeftAndRightEdgeContent):
                this._appSettingsService.SyncLeftAndRightEdgeContent
                    = this.StackCatalogViewModel.SyncLeftAndRightEdgeContent;
                break;

            case nameof(MainViewModel.SyncTopAndBottomEdgeContent):
                this._appSettingsService.SyncTopAndBottomEdgeContent
                    = this.StackCatalogViewModel.SyncTopAndBottomEdgeContent;
                break;

            case nameof(MainViewModel.SyncAllEdgeContent):
                this._appSettingsService.SyncAllEdgeContent = this.StackCatalogViewModel.SyncAllEdgeContent;
                break;
        }
    }

    private void SaveEdgeWindowPresentation(EdgeShelfSide side)
    {
        this._appSettingsService.SetEdgeWindowSizeMode(
            side,
            this.StackCatalogViewModel.GetEdgeWindowSizeMode(side));
        this._appSettingsService.SetEdgeWindowAlignment(
            side,
            this.StackCatalogViewModel.GetEdgeWindowAlignment(side));
    }

    private void UpdateEdgeMenuState()
    {
        var canOpenEdgeWindows = this.StackCatalogViewModel.HasEnabledEdgeWindows &&
                                 !this.StackCatalogViewModel.EdgeWindowsPaused &&
                                 !this.StackCatalogViewModel.IsGameModeSuppressing;
        if (this._edgeShelfMenu is not null)
        {
            this._edgeShelfMenu.IsEnabled = canOpenEdgeWindows;
        }

        foreach (var (side, menuItem) in this._edgeShelfMenuItems)
        {
            menuItem.IsEnabled = canOpenEdgeWindows && this.StackCatalogViewModel.IsEdgeWindowEnabled(side);
        }
    }

    private void OnSystemColorValuesChanged(UISettings sender, object args) =>
        this.RunOnUiThread(() =>
        {
            this.StackCatalogViewModel.RefreshSystemColors();
            this.DropCommandCatalogViewModel.RefreshSystemColors();
        });

    private void QueueCatalogSave()
    {
        if (!this._dispatcherQueue.HasThreadAccess)
        {
            this.RunOnUiThread(this.QueueCatalogSave);
            return;
        }

        var previousSave = this._catalogSaveDebounce;
        var pendingSave = new CancellationTokenSource();
        this._catalogSaveDebounce = pendingSave;
        previousSave?.Cancel();
        _ = this.PersistCatalogAfterDelayAsync(this.CreateCatalogSnapshot(), pendingSave);
    }

    private void QueueDropCommandSave()
    {
        if (!this._dispatcherQueue.HasThreadAccess)
        {
            this.RunOnUiThread(this.QueueDropCommandSave);
            return;
        }

        var previousSave = this._dropCommandSaveDebounce;
        var pendingSave = new CancellationTokenSource();
        this._dropCommandSaveDebounce = pendingSave;
        previousSave?.Cancel();
        _ = this.PersistDropCommandCatalogAfterDelayAsync(
            this.CreateDropCommandCatalogSnapshot(),
            pendingSave);
    }

    private async Task PersistCatalogAfterDelayAsync(
        StackCatalogState snapshot,
        CancellationTokenSource pendingSave)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250), pendingSave.Token);
            await this._stackRepository.SaveAsync(snapshot);
        }
        catch (OperationCanceledException) when (pendingSave.IsCancellationRequested)
        {
            // A newer snapshot superseded this one.
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"OmniTray could not save the stack catalogue: {exception}");
        }
        finally
        {
            if (ReferenceEquals(this._catalogSaveDebounce, pendingSave))
            {
                this._catalogSaveDebounce = null;
            }

            pendingSave.Dispose();
        }
    }

    private StackCatalogState CreateCatalogSnapshot() => new(
        this.StackCatalogViewModel.Stacks.Select(static stack => stack.Model).ToArray(),
        this._windows?.GetOpenTrayWindowStates() ?? [], this.StackCatalogViewModel.GetEdgeShelfStates());

    private async Task PersistDropCommandCatalogAfterDelayAsync(
        DropCommandCatalogState snapshot,
        CancellationTokenSource pendingSave)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250), pendingSave.Token);
            await this._dropCommandRepository.SaveAsync(snapshot);
        }
        catch (OperationCanceledException) when (pendingSave.IsCancellationRequested)
        {
            // A newer snapshot superseded this one.
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"OmniTray could not save the drop command catalogue: {exception}");
        }
        finally
        {
            if (ReferenceEquals(this._dropCommandSaveDebounce, pendingSave))
            {
                this._dropCommandSaveDebounce = null;
            }

            pendingSave.Dispose();
        }
    }

    private DropCommandCatalogState CreateDropCommandCatalogSnapshot() =>
        this.DropCommandCatalogViewModel.CreateSnapshot(
            this._windows?.GetOpenDropCommandWindowStates() ?? []);

    private async Task SaveCatalogNowAsync()
    {
        var pendingSave = this._catalogSaveDebounce;
        this._catalogSaveDebounce = null;
        pendingSave?.Cancel();
        await this._stackRepository.SaveAsync(this.CreateCatalogSnapshot());
    }

    private async Task SaveDropCommandCatalogNowAsync()
    {
        var pendingSave = this._dropCommandSaveDebounce;
        this._dropCommandSaveDebounce = null;
        pendingSave?.Cancel();
        await this._dropCommandRepository.SaveAsync(this.CreateDropCommandCatalogSnapshot());
    }

    private void RunOnUiThread(Action action)
    {
        if (this._dispatcherQueue.HasThreadAccess)
        {
            action();
            return;
        }

        _ = this._dispatcherQueue.TryEnqueue(() => action());
    }

    private async void ExitApplication()
    {
        try
        {
            await Task.WhenAll(this.SaveCatalogNowAsync(), this.SaveDropCommandCatalogNowAsync());
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"OmniTray could not flush the stack catalogue during exit: {exception}");
        }
        finally
        {
            this._trayIcon?.Dispose();
            this._trayIcon = null;
            // this._windows?.CloseAll();
            Environment.Exit(0);
        }
    }
}
