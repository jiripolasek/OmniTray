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
    private bool _isSynchronizingViewButtons;

    internal StackContentsPage(StackContentsViewModel viewModel, Window owner)
    {
        this.ViewModel = viewModel;
        this.InitializeComponent();
        this.ItemsOrganizer.DialogOwner = owner;
        this.ItemsOrganizer.SelectedItemsChanged += this.OnSelectedItemsChanged;
    }

    public StackContentsViewModel ViewModel { get; }
    internal event EventHandler? BackRequested;
    internal event EventHandler? SelectedItemsChanged;
    internal void RevealItem(Guid id) => this.ItemsOrganizer.SelectItem(id);
    internal void FocusList() => this.ItemsOrganizer.FocusItemList();

    internal void SetStack(DropStackViewModel? stack, bool fromSearch = false)
    {
        this.ViewModel.SetStack(stack, fromSearch);
        this.ItemsOrganizer.Stack = stack;
        NoteMenu.SetStack(this.EditorNotesMenu, stack);
        if (stack is not null) { this.ApplyStackViewMode(stack.InspectorViewMode); }
        this.OnSelectedItemsChanged(this, EventArgs.Empty);
    }

    private void OnBackToOverviewClick(object sender, RoutedEventArgs args) => this.BackRequested?.Invoke(this, EventArgs.Empty);

    private void OnSelectedItemsChanged(object? sender, EventArgs args)
    {
        this.ViewModel.SetSelection(this.ItemsOrganizer.PrimarySelectedItem, this.ItemsOrganizer.SelectedItemCount);
        this.SelectedItemsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ChangeStackViewMode(StackInspectorViewMode viewMode)
    {
        if (this._isSynchronizingViewButtons || this.ViewModel.Stack is null) { return; }
        this.ViewModel.ChangeViewMode(viewMode);
        this.ApplyStackViewMode(viewMode);
    }

    private void ApplyStackViewMode(StackInspectorViewMode viewMode)
    {
        this._isSynchronizingViewButtons = true;
        try
        {
            this.StackListViewItem.IsChecked = viewMode == StackInspectorViewMode.List;
            this.StackGridViewItem.IsChecked = viewMode == StackInspectorViewMode.Grid;
            this.StackViewIcon.Glyph = viewMode == StackInspectorViewMode.Grid ? "\uE8A9" : "\uEA37";
            ToolTipService.SetToolTip(
                this.StackViewButton,
                viewMode == StackInspectorViewMode.Grid ? "Item layout: Grid" : "Item layout: List");
            this.ItemsOrganizer.SetThumbnailView(viewMode == StackInspectorViewMode.Grid);
        }
        finally
        {
            this._isSynchronizingViewButtons = false;
        }
    }

    private void OnListViewClick(object sender, RoutedEventArgs args) =>
        this.ChangeStackViewMode(StackInspectorViewMode.List);

    private void OnThumbnailViewClick(object sender, RoutedEventArgs args) =>
        this.ChangeStackViewMode(StackInspectorViewMode.Grid);

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
        App.Current.ShowToast($"Placed {stack.Name} on the {side.GetDisplayName().ToLowerInvariant()} edge.", InfoBarSeverity.Success);
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
        this.SetStack(null);
        this.ItemsOrganizer.DialogOwner = null;
    }
}
