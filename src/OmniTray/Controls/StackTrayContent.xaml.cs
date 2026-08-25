// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using Windows.ApplicationModel.DataTransfer;
using Microsoft.UI.Xaml.Input;

namespace OmniTray.Controls;

internal sealed partial class StackTrayContent : UserControl, ITrayWindowContent
{
    private readonly TrayInspectorPopup _inspectorPopup;
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
        this.InspectorPopup.PlacementTarget = isMinimal ? this.MinimalContent : this.ExploreButton;
        this._inspectorPopup = new TrayInspectorPopup(owner, this.InspectorPopup, viewModel);
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
                "Rename",
                Symbol.Rename,
                () => this._inspectorPopup.Show(TrayInspectorMode.Rename)),
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
        this._inspectorPopup.Dispose();
    }

    private void OnDragOver(object sender, DragEventArgs args)
    {
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

    private void OnStackDragStarting(UIElement sender, DragStartingEventArgs args) =>
        DragDropDataService.Write(
            args.Data,
            this.ViewModel.Model,
            this.ViewModel.Name,
            App.Current.AllowMoveOnDragOutPreference);

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
