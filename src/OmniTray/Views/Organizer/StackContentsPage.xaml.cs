// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

using Microsoft.UI.Xaml.Input;
using OmniTray.Controls;
using OmniTray.ViewModels.Organizer;

namespace OmniTray.Views.Organizer;

public sealed partial class StackContentsPage : Page, IDisposable
{
    internal event EventHandler? BackRequested;
    internal event EventHandler? DetailsPaneToggleRequested;
    internal event EventHandler? SelectedItemsChanged;
    public StackContentsViewModel ViewModel { get; }

    internal StackContentsPage(StackContentsViewModel viewModel)
    {
        this.ViewModel = viewModel;
        this.InitializeComponent();
        OrganizerKeyboardAccelerators.ScopeTo(
            this.ItemsOrganizer,
            this.RenameTitleButton,
            this.CopySelectedItemsButton,
            this.OpenSelectedItemContainerButton,
            this.RemoveSelectedItemsButton);
        this.ItemsOrganizer.SelectedItemsChanged += this.OnSelectedItemsChanged;
        this.ItemsOrganizer.SelectionCommandsChanged += this.OnSelectionCommandsChanged;
    }

    internal void RevealItem(Guid id) => this.ItemsOrganizer.SelectItem(id);
    internal void FocusList() => this.ItemsOrganizer.FocusItemList();

    internal void SetDetailsPaneState(bool isVisible, bool isAvailable)
        => this.CommandToolbar.SetDetailsPaneState(isVisible, isAvailable);

    internal void SetStack(DropStackViewModel? stack, bool fromSearch = false)
    {
        this.ViewModel.SetStack(stack, fromSearch);
        this.ItemsOrganizer.Stack = stack;
        NoteMenu.SetStack(this.EditorNotesMenu, stack);
        this.ApplyCollectionViewMode(this.ViewModel.LayoutMode);

        this.OnSelectedItemsChanged(this, EventArgs.Empty);
    }

    private void OnBackToOverviewClick(object sender, RoutedEventArgs args) =>
        this.BackRequested?.Invoke(this, EventArgs.Empty);

    private void OnDetailsPaneToggleClick(object? sender, EventArgs args) =>
        this.DetailsPaneToggleRequested?.Invoke(this, EventArgs.Empty);

    private void OnSelectedItemsChanged(object? sender, EventArgs args)
    {
        this.ViewModel.SetSelection(this.ItemsOrganizer.PrimarySelectedItem, this.ItemsOrganizer.SelectedItemCount);
        this.UpdateSelectionCommandSurface();
        this.SelectedItemsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnSelectionCommandsChanged(object? sender, EventArgs args) =>
        this.UpdateSelectionCommandSurface();

    private void UpdateSelectionCommandSurface()
    {
        var hasSelection = this.ItemsOrganizer.SelectedItemCount > 0;
        this.CommandToolbar.IsSelectionActive = hasSelection;
        this.SelectionSummaryText.Visibility = hasSelection ? Visibility.Collapsed : Visibility.Visible;
        if (hasSelection)
        {
            this.OpenSelectedItemsButton.Visibility = this.ItemsOrganizer.CanOpenSelectedItem
                ? Visibility.Visible
                : Visibility.Collapsed;
            var canCopy = this.ItemsOrganizer.CanCopySelectedItems;
            this.CopySelectedItemsButton.IsEnabled = canCopy;
            this.CopySelectedItemsButton.Visibility = canCopy
                ? Visibility.Visible
                : Visibility.Collapsed;
            var canOpenContainer = this.ItemsOrganizer.CanOpenSelectedItemContainer;
            this.OpenSelectedItemContainerButton.IsEnabled = canOpenContainer;
            this.OpenSelectedItemContainerButton.Visibility = canOpenContainer
                ? Visibility.Visible
                : Visibility.Collapsed;
            this.MoveSelectedItemsUpButton.IsEnabled = this.ItemsOrganizer.CanMoveSelectedItemsUp;
            this.MoveSelectedItemsDownButton.IsEnabled = this.ItemsOrganizer.CanMoveSelectedItemsDown;
            this.SplitSelectedItemsButton.IsEnabled = this.ItemsOrganizer.CanSplitSelectedItems;
            this.RemoveSelectedItemsButton.IsEnabled = this.ItemsOrganizer.CanChangeSelectedItems;
            this.DuplicateSelectedItemsButton.IsEnabled = this.ItemsOrganizer.CanChangeSelectedItems;
            this.CutSelectedItemsButton.Visibility = this.ItemsOrganizer.CanCutSelectedItems
                ? Visibility.Visible
                : Visibility.Collapsed;
            this.DeleteSelectedItemsFromDiskButton.Visibility = this.ItemsOrganizer.CanDeleteSelectedItemsFromDisk
                ? Visibility.Visible
                : Visibility.Collapsed;
            this.DeleteSelectedItemsFromDiskButton.IsEnabled = this.ItemsOrganizer.CanChangeSelectedItems;
        }

    }

    private void OnClearSelectionClick(object? sender, EventArgs args) =>
        this.ItemsOrganizer.ClearSelection();

    private async void OnOpenSelectedItemsClick(object sender, RoutedEventArgs args) =>
        await this.ItemsOrganizer.OpenSelectedItemAsync();

    private void OnCopySelectedItemsClick(object sender, RoutedEventArgs args) =>
        this.ItemsOrganizer.CopySelectedItems();

    private async void OnOpenSelectedItemContainerClick(object sender, RoutedEventArgs args) =>
        await this.ItemsOrganizer.OpenSelectedItemContainerAsync();

    private void OnMoveSelectedItemsUpClick(object sender, RoutedEventArgs args) =>
        this.ItemsOrganizer.MoveSelectedItems(-1);

    private void OnMoveSelectedItemsDownClick(object sender, RoutedEventArgs args) =>
        this.ItemsOrganizer.MoveSelectedItems(1);

    private void OnSplitSelectedItemsClick(object sender, RoutedEventArgs args) =>
        this.ItemsOrganizer.SplitSelectedItems();

    private async void OnRemoveSelectedItemsClick(object sender, RoutedEventArgs args) =>
        await this.ItemsOrganizer.RemoveSelectedItemsAsync();

    private async void OnDuplicateSelectedItemsClick(object sender, RoutedEventArgs args) =>
        await this.ItemsOrganizer.DuplicateSelectedItemsAsync();

    private void OnCutSelectedItemsClick(object sender, RoutedEventArgs args) =>
        this.ItemsOrganizer.CutSelectedItems();

    private async void OnDeleteSelectedItemsFromDiskClick(object sender, RoutedEventArgs args) =>
        await this.ItemsOrganizer.DeleteSelectedItemsFromDiskAsync();

    private void OnCollectionViewModeChanged(object? sender, EventArgs args) =>
        this.ApplyCollectionViewMode(this.CommandToolbar.CollectionViewMode);

    private void ApplyCollectionViewMode(OrganizerCollectionViewMode viewMode)
    {
        this.ViewModel.LayoutMode = viewMode;
        this.CommandToolbar.CollectionViewMode = viewMode;
        this.ItemsOrganizer.SetOrganizerViewMode(viewMode);
    }

    private async void OnPasteIntoStackClick(object sender, RoutedEventArgs args)
    {
        if (this.ViewModel.Stack is { } stack)
        {
            await App.Current.InsertClipboardContentAsync(stack);
        }
    }

    private void OnPopOutSelectedStackClick(object sender, RoutedEventArgs args)
    {
        if (this.ViewModel.Stack is { } stack)
        {
            App.Current.OpenTray(stack);
        }
    }

    private async void OnRenameSelectedStackClick(object sender, RoutedEventArgs args)
    {
        if (this.ViewModel.Stack is { } stack)
        {
            await StackDialogService.RenameAsync(this.RootGrid.XamlRoot, stack);
        }
    }

    private void OnRenameTitlePointerChanged(object sender, PointerRoutedEventArgs args) =>
        this.UpdateRenameEditIconVisibility();

    private void OnRenameTitleFocusChanged(object sender, RoutedEventArgs args) =>
        this.UpdateRenameEditIconVisibility();

    private void UpdateRenameEditIconVisibility() =>
        this.RenameEditIcon.Opacity = this.RenameTitleButton.IsPointerOver ||
                                      this.RenameTitleButton.FocusState != FocusState.Unfocused
            ? 1
            : 0;

    private async void OnDeleteSelectedStackClick(object sender, RoutedEventArgs args)
    {
        if (this.ViewModel.Stack is { } stack)
        {
            await this.DeleteStackAsync(stack);
        }
    }

    private async Task DeleteStackAsync(DropStackViewModel stack)
    {
        if (!await StackDialogService.ConfirmDeleteAsync(this.RootGrid.XamlRoot, stack))
        {
            return;
        }

        await App.Current.DeleteStackAsync(stack);
        App.Current.ShowToast($"Deleted {stack.Name}.", InfoBarSeverity.Success);
    }

    private void OnEdgeAssignmentFlyoutOpening(object sender, object args)
    {
        var assignedEdge = this.ViewModel.Stack?.AssignedEdge;
        this.NoEdgeAssignmentItem.IsChecked = assignedEdge is null;
        this.LeftEdgeAssignmentItem.IsChecked = assignedEdge == EdgeShelfSide.Left;
        this.RightEdgeAssignmentItem.IsChecked = assignedEdge == EdgeShelfSide.Right;
        this.TopEdgeAssignmentItem.IsChecked = assignedEdge == EdgeShelfSide.Top;
        this.BottomEdgeAssignmentItem.IsChecked = assignedEdge == EdgeShelfSide.Bottom;
    }

    private void OnAssignLeftEdgeClick(object sender, RoutedEventArgs args) =>
        this.AssignEditorStackToEdge(EdgeShelfSide.Left);

    private void OnAssignRightEdgeClick(object sender, RoutedEventArgs args) =>
        this.AssignEditorStackToEdge(EdgeShelfSide.Right);

    private void OnAssignTopEdgeClick(object sender, RoutedEventArgs args) =>
        this.AssignEditorStackToEdge(EdgeShelfSide.Top);

    private void OnAssignBottomEdgeClick(object sender, RoutedEventArgs args) =>
        this.AssignEditorStackToEdge(EdgeShelfSide.Bottom);

    private void AssignEditorStackToEdge(EdgeShelfSide side)
    {
        if (this.ViewModel.Stack is not { } stack || !this.ViewModel.AssignToEdge(side)) { return; }

        App.Current.ShowToast($"Placed {stack.Name} on the {side.GetDisplayName().ToLowerInvariant()} edge.",
            InfoBarSeverity.Success);
        this.ViewModel.Refresh();
    }

    private void OnRemoveEdgeAssignmentClick(object sender, RoutedEventArgs args)
    {
        if (this.ViewModel.Stack is not { } stack || !this.ViewModel.AssignToEdge(null)) { return; }

        App.Current.ShowToast($"Removed {stack.Name} from the edge shelves.", InfoBarSeverity.Success);
        this.ViewModel.Refresh();
    }

    public void Dispose()
    {
        this.ItemsOrganizer.SelectedItemsChanged -= this.OnSelectedItemsChanged;
        this.ItemsOrganizer.SelectionCommandsChanged -= this.OnSelectionCommandsChanged;
        this.SetStack(null);
    }
}
