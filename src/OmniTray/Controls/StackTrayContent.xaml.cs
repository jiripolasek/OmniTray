// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using Windows.ApplicationModel.DataTransfer;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Input;

namespace OmniTray.Controls;

internal sealed partial class StackTrayContent : UserControl, ITrayWindowContent
{
    private static readonly TimeSpan InspectorHoverDelay = TimeSpan.FromMilliseconds(700);
    private readonly TrayInspectorPopup _inspectorPopup;
    private readonly DispatcherQueueTimer _inspectorHoverTimer;
    private FrameworkElement? _inspectorHoverTarget;
    private bool _isDisposed;

    public StackTrayContent(Window owner, DropStackViewModel viewModel, bool isMinimal)
    {
        ArgumentNullException.ThrowIfNull(owner);
        this.ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        this.IsMinimal = isMinimal;
        this.InitializeComponent();
        this.NormalContent.Visibility = isMinimal ? Visibility.Collapsed : Visibility.Visible;
        this.MinimalContent.Visibility = isMinimal ? Visibility.Visible : Visibility.Collapsed;
        this.DropHintOverlay.Visibility = Visibility.Collapsed;
        this._inspectorHoverTimer = this.DispatcherQueue.CreateTimer();
        this._inspectorHoverTimer.Interval = InspectorHoverDelay;
        this._inspectorHoverTimer.IsRepeating = false;
        this._inspectorHoverTimer.Tick += this.OnInspectorHoverTimerTick;
        this._inspectorPopup = new TrayInspectorPopup(
            owner,
            this.InspectorPopup,
            this.InspectorPlacementTarget,
            viewModel,
            TrayInspectorPlacement.Bottom);
        this.ContextActions =
        [
            new TrayContextAction(
                "Explore items",
                Symbol.View,
                () => this._inspectorPopup.Show(TrayInspectorMode.Browse)),
            new TrayContextAction(
                "Insert Clipboard content",
                Symbol.Paste,
                () => _ = App.Current.InsertClipboardContentAsync(this.ViewModel)),
            new TrayContextAction(
                "Customize",
                Symbol.Rename,
                () => this._inspectorPopup.Show(TrayInspectorMode.Customize)),
            new TrayContextAction(
                "Delete stack",
                Symbol.Delete,
                () => _ = this._inspectorPopup.ConfirmDeleteAsync(),
                true)
        ];
    }

    public DropStackViewModel ViewModel { get; }

    public bool IsMinimal { get; }

    public FrameworkElement View => this;

    public IReadOnlyList<TrayContextAction> ContextActions { get; }

    public void PrepareForClose(Action completed) => this._inspectorPopup.PrepareForClose(completed);

    public void Dispose()
    {
        if (this._isDisposed)
        {
            return;
        }

        this._isDisposed = true;
        this.CancelInspectorHover();
        this._inspectorHoverTimer.Tick -= this.OnInspectorHoverTimerTick;
        this._inspectorPopup.Dispose();
    }

    private void OnDragOver(object sender, DragEventArgs args)
    {
        this.CancelInspectorHover();
        args.Handled = true;
        if (DragDropDataService.HasStackReference(args.DataView))
        {
            args.AcceptedOperation = DataPackageOperation.None;
            args.DragUIOverride.Caption = DragDropDataService.ActiveStackReferenceId == this.ViewModel.Model.Id
                ? "Stack is already here"
                : "Use Combine to merge stacks";
            args.DragUIOverride.IsCaptionVisible = true;
            args.DragUIOverride.IsContentVisible = true;
            this.SetDropHintVisible(false);
            return;
        }

        if (DragDropDataService.HasItemReference(args.DataView))
        {
            this.ConfigureItemTransferDragOver(args, $"Add to {this.ViewModel.Name}");
            this.SetDropHintVisible(args.AcceptedOperation != DataPackageOperation.None);
            return;
        }

        if (!DragDropDataService.HasSupportedFormat(args.DataView))
        {
            args.AcceptedOperation = DataPackageOperation.None;
            this.SetDropHintVisible(false);
            return;
        }

        args.AcceptedOperation = DataPackageOperation.Copy;
        args.DragUIOverride.Caption = $"Add to {this.ViewModel.Name}";
        args.DragUIOverride.IsCaptionVisible = true;
        args.DragUIOverride.IsContentVisible = true;
        this.SetDropHintVisible(true);
    }

    private void OnDragLeave(object sender, DragEventArgs args)
    {
        args.Handled = true;
        this.SetDropHintVisible(false);
    }

    private async void OnDrop(object sender, DragEventArgs args)
    {
        args.Handled = true;
        this.SetDropHintVisible(false);

        if (DragDropDataService.HasStackReference(args.DataView))
        {
            return;
        }

        if (DragDropDataService.HasItemReference(args.DataView))
        {
            if (DragDropDataService.ActiveItemReference?.SourceStackId == this.ViewModel.Model.Id)
            {
                return;
            }

            await this.TransferItemsAsync(
                args.DataView,
                this.ViewModel.Items.Count,
                IsCopyRequested(args));
            return;
        }

        try
        {
            var items = await DragDropDataService.ReadAsync(args.DataView);
            if (items.Count > 0)
            {
                this.ViewModel.AppendDroppedItems(items);
            }
        }
        catch
        {
            // Keep the existing stack intact when a source becomes unavailable mid-drop.
        }
    }

    private void OnStackDragStarting(UIElement sender, DragStartingEventArgs args)
    {
        this.CancelInspectorHover();
        DragDropDataService.Write(
            args.Data,
            this.ViewModel.Model,
            this.ViewModel.Name,
            App.Current.AllowMoveOnDragOutPreference);
    }

    private async void OnStackDropCompleted(UIElement sender, DropCompletedEventArgs args) =>
        await App.Current.CompleteStackDragAsync(args.DropResult);

    private async Task TransferItemsAsync(DataPackageView dataView, int targetIndex, bool copy)
    {
        var itemReference = await DragDropDataService.ReadItemReferenceAsync(dataView);
        if (itemReference is null)
        {
            return;
        }

        copy = copy && itemReference.SourceStackId != this.ViewModel.Model.Id;
        try
        {
            await App.Current.TransferItemsAsync(
                itemReference,
                this.ViewModel,
                targetIndex,
                copy);
        }
        catch
        {
            // Keep both stacks intact when a source becomes unavailable mid-transfer.
        }
    }

    private void ConfigureItemTransferDragOver(DragEventArgs args, string caption)
    {
        var sameStack = DragDropDataService.ActiveItemReference?.SourceStackId == this.ViewModel.Model.Id;
        if (sameStack)
        {
            args.AcceptedOperation = DataPackageOperation.None;
            args.DragUIOverride.Caption = "Item is already in this stack";
            args.DragUIOverride.IsCaptionVisible = true;
            args.DragUIOverride.IsContentVisible = true;
            return;
        }

        var copy = IsCopyRequested(args);
        args.AcceptedOperation = copy
            ? DataPackageOperation.Copy
            : DragDropDataService.GetAcceptedInternalMoveOperation(args.DataView);
        args.DragUIOverride.Caption = copy ? $"Copy — {caption}" : caption;
        args.DragUIOverride.IsCaptionVisible = true;
        args.DragUIOverride.IsContentVisible = true;
    }

    private static bool IsCopyRequested(DragEventArgs args) =>
        (args.Modifiers & DragDropModifiers.Control) != 0;

    private void OnExploreClick(object sender, RoutedEventArgs args) =>
        this._inspectorPopup.Show(TrayInspectorMode.Browse);

    private void OnInspectorHoverPointerEntered(object sender, PointerRoutedEventArgs args)
    {
        if (!App.Current.OpenInspectorOnHoverPreference ||
            args.Pointer.PointerDeviceType != PointerDeviceType.Mouse ||
            DragDropDataService.HasActiveDrag ||
            sender is not FrameworkElement target)
        {
            return;
        }

        this._inspectorHoverTarget = target;
        this._inspectorHoverTimer.Stop();
        this._inspectorHoverTimer.Start();
    }

    private void OnInspectorHoverPointerExited(object sender, PointerRoutedEventArgs args)
    {
        if (ReferenceEquals(this._inspectorHoverTarget, sender))
        {
            this.CancelInspectorHover();
        }
    }

    private void OnInspectorHoverPointerPressed(object sender, PointerRoutedEventArgs args) =>
        this.CancelInspectorHover();

    private void OnInspectorHoverTimerTick(DispatcherQueueTimer sender, object args)
    {
        sender.Stop();
        var target = this._inspectorHoverTarget;
        this._inspectorHoverTarget = null;
        if (this._isDisposed ||
            !App.Current.OpenInspectorOnHoverPreference ||
            target is null ||
            target.XamlRoot is null ||
            DragDropDataService.HasActiveDrag)
        {
            return;
        }

        this._inspectorPopup.Show(TrayInspectorMode.Browse);
    }

    private void CancelInspectorHover()
    {
        this._inspectorHoverTarget = null;
        this._inspectorHoverTimer.Stop();
    }

    private void SetDropHintVisible(bool isVisible)
    {
        this.DropOutline.Visibility = this.IsMinimal && isVisible
            ? Visibility.Visible
            : Visibility.Collapsed;
        this.DropHintOverlay.Visibility = !this.IsMinimal && isVisible
            ? Visibility.Visible
            : Visibility.Collapsed;
    }
}
