// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using Windows.ApplicationModel.DataTransfer;

namespace OmniTray.Views;

public sealed partial class StackOrganizerWindow
{
    private void OnEdgeNavigationDragOver(object sender, DragEventArgs args)
    {
        args.Handled = true;
        this._overviewPage.ClearInsertionAdorner();
        args.AcceptedOperation = DataPackageOperation.None;
        var caption = "Drop a stack here to move it to this edge";
        if (sender is NavigationViewItem { Tag: StackOrganizerScopeViewModel { Side: { } side } } &&
            DragDropDataService.HasStackReference(args.DataView))
        {
            var source = DragDropDataService.ActiveStackReferenceId is { } stackId
                ? this.ViewModel.Catalog.Stacks.FirstOrDefault(stack => stack.Model.Id == stackId)
                : null;
            if (source is not null && this.ViewModel.Catalog.GetEdgeStacks(side).Contains(source))
            {
                // GetEdgeStacks resolves shared edges, so this also rejects moves within a shared collection.
                caption = "Stack is already on this edge";
            }
            else
            {
                args.AcceptedOperation = DragDropDataService.GetAcceptedInternalMoveOperation(args.DataView);
                caption = $"Move to the {side.GetDisplayName().ToLowerInvariant()} edge";
                if (!this.ViewModel.Catalog.IsEdgeWindowEnabled(side))
                {
                    caption += " (edge window is disabled)";
                }
            }
        }

        args.DragUIOverride.Caption = caption;
        args.DragUIOverride.IsCaptionVisible = true;
        args.DragUIOverride.IsContentVisible = true;
        args.DragUIOverride.IsGlyphVisible = true;
    }

    private async void OnEdgeNavigationDrop(object sender, DragEventArgs args)
    {
        args.Handled = true;
        this._overviewPage.ClearInsertionAdorner();
        args.AcceptedOperation = DataPackageOperation.None;
        if (sender is not NavigationViewItem { Tag: StackOrganizerScopeViewModel { Side: { } side } } ||
            !DragDropDataService.HasStackReference(args.DataView))
        {
            return;
        }

        var deferral = args.GetDeferral();
        try
        {
            // Reading the private reference marks the drop as internal, avoiding external-move cleanup.
            var stackId = await DragDropDataService.ReadStackReferenceAsync(args.DataView);
            var stack = stackId is { } id
                ? this.ViewModel.Catalog.Stacks.FirstOrDefault(candidate => candidate.Model.Id == id)
                : null;
            if (stack is null)
            {
                App.Current.ShowToast("That stack is no longer available.", InfoBarSeverity.Warning);
                return;
            }

            if (this.ViewModel.Catalog.AssignStackToEdge(stack, side))
            {
                args.AcceptedOperation = DragDropDataService.GetAcceptedInternalMoveOperation(args.DataView);
                App.Current.ShowToast(
                    $"Moved {stack.Name} to the {side.GetDisplayName().ToLowerInvariant()} edge.",
                    InfoBarSeverity.Success);
            }
        }
        catch (Exception exception)
        {
            App.Current.ShowToast($"The stack could not be moved: {exception.Message}", InfoBarSeverity.Error);
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void OnNewStackNavigationDragOver(object sender, DragEventArgs args)
    {
        args.Handled = true;
        this._overviewPage.ClearInsertionAdorner();
        args.AcceptedOperation = DataPackageOperation.None;
        var caption = "Drop items here to create a new stack";
        if (!DragDropDataService.HasStackReference(args.DataView) &&
            DragDropDataService.HasSupportedFormat(args.DataView))
        {
            var isItemTransfer = DragDropDataService.HasItemReference(args.DataView);
            var copy = !isItemTransfer || (args.Modifiers & DragDropModifiers.Control) != 0;
            args.AcceptedOperation = copy
                ? DataPackageOperation.Copy
                : DragDropDataService.GetAcceptedInternalMoveOperation(args.DataView);
            caption = this.Navigation.ScopeSide is { } side
                ? $"Create a new stack on the {side.GetDisplayName().ToLowerInvariant()} edge"
                : "Create a new stack";
            if (isItemTransfer)
            {
                caption = $"{(copy ? "Copy" : "Move")} items — {caption}";
            }
        }

        args.DragUIOverride.Caption = caption;
        args.DragUIOverride.IsCaptionVisible = true;
        args.DragUIOverride.IsContentVisible = true;
        args.DragUIOverride.IsGlyphVisible = true;
    }

    private async void OnNewStackNavigationDrop(object sender, DragEventArgs args)
    {
        args.Handled = true;
        this._overviewPage.ClearInsertionAdorner();
        args.AcceptedOperation = DataPackageOperation.None;
        if (DragDropDataService.HasStackReference(args.DataView) ||
            !DragDropDataService.HasSupportedFormat(args.DataView))
        {
            return;
        }

        var scopeSide = this.Navigation.ScopeSide;
        var copy = (args.Modifiers & DragDropModifiers.Control) != 0;
        var deferral = args.GetDeferral();
        try
        {
            DropStackViewModel stack;
            if (DragDropDataService.HasItemReference(args.DataView))
            {
                var created = await this.ViewModel.CreateStackFromItemDropAsync(args.DataView, copy);
                if (created is null)
                {
                    return;
                }

                stack = created;
                args.AcceptedOperation = copy
                    ? DataPackageOperation.Copy
                    : DragDropDataService.GetAcceptedInternalMoveOperation(args.DataView);
            }
            else
            {
                var items = await DragDropDataService.ReadAsync(args.DataView);
                if (items.Count == 0)
                {
                    App.Current.ShowToast("This drag did not contain a supported payload.", InfoBarSeverity.Warning);
                    return;
                }

                stack = this.ViewModel.Catalog.AddStack(DropStack.Create(items));
                args.AcceptedOperation = DataPackageOperation.Copy;
            }

            this.OpenCreatedStack(stack, scopeSide);
            App.Current.ShowToast(
                $"Created a new stack with {stack.Model.Items.Count} {(stack.Model.Items.Count == 1 ? "item" : "items")}.",
                InfoBarSeverity.Success);
        }
        catch (Exception exception)
        {
            App.Current.ShowToast($"The stack could not be created: {exception.Message}", InfoBarSeverity.Error);
        }
        finally
        {
            deferral.Complete();
        }
    }
}
