// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

using System.Numerics;
using Windows.System;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using OmniTray.Controls;
using OmniTray.ViewModels.Organizer;
using DispatcherQueuePriority = Microsoft.UI.Dispatching.DispatcherQueuePriority;

namespace OmniTray.Views.Organizer;

public sealed partial class NoteLibraryPage : Page, IDisposable
{
    private const float NoteHoverElevation = 16;

    internal event EventHandler? SelectedNoteChanged;
    internal event EventHandler? DetailsPaneToggleRequested;
    private FrameworkElement? _hoveredNoteRow;
    private bool _isThumbnailView;
    private bool _isSynchronizingSelection;

    public NoteLibraryViewModel ViewModel { get; }
    internal Guid? SelectedNoteId => this.ViewModel.SelectedNoteId;

    internal NoteLibraryPage(MainViewModel catalog)
    {
        this.ViewModel = new NoteLibraryViewModel(catalog);
        this.InitializeComponent();
        OrganizerKeyboardAccelerators.ScopeTo(this.NotesList, this.GoToStackButton, this.DeleteButton);
        this.ViewModel.SelectedNoteChanged += this.OnSelectedNoteChanged;
        this.ApplyCollectionViewMode(this.ViewModel.LayoutMode);
    }

    internal void SetActive(bool active) => this.ViewModel.SetActive(active);
    internal void FocusList() => this.NotesList.Focus(FocusState.Keyboard);
    internal void ShowDeleted(bool deleted) => this.ModeBox.SelectedIndex = deleted ? 1 : 0;
    internal void SetDetailsPaneState(bool isVisible, bool isAvailable) =>
        this.CommandToolbar.SetDetailsPaneState(isVisible, isAvailable);

    private void OnModeChanged(object sender, SelectionChangedEventArgs args) =>
        this.ViewModel.ShowDeleted = ((ComboBox)sender).SelectedIndex == 1;

    private void OnSearchChanged(object sender, TextChangedEventArgs args) =>
        this.ViewModel.FilterText = ((TextBox)sender).Text;

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (!this._isSynchronizingSelection)
        {
            this.ViewModel.SetSelection(this.GetSelectedEntries());
        }

        this.CommandToolbar.IsSelectionActive = this.NotesList.SelectedItems.Count > 0;
        this.UpdateNoteSelectionIndicators();
    }

    private void OnSelectedNoteChanged(object? sender, EventArgs args)
    {
        this.SynchronizeSelectionFromViewModel();
        this.CommandToolbar.IsSelectionActive = this.ViewModel.HasSelection;
        this.UpdateNoteSelectionIndicators();
        this.SelectedNoteChanged?.Invoke(this, args);
    }

    private void OnClearSelectionClick(object? sender, EventArgs args) => this.NotesList.SelectedItems.Clear();

    private void OnDetailsPaneToggleClick(object? sender, EventArgs args) =>
        this.DetailsPaneToggleRequested?.Invoke(this, EventArgs.Empty);

    private void OnCollectionViewModeChanged(object? sender, EventArgs args) =>
        this.ApplyCollectionViewMode(this.CommandToolbar.CollectionViewMode);

    private void ApplyCollectionViewMode(OrganizerCollectionViewMode viewMode)
    {
        this.ViewModel.LayoutMode = viewMode;
        this.CommandToolbar.CollectionViewMode = viewMode;
        this._isThumbnailView = viewMode != OrganizerCollectionViewMode.List;
        this.NotesList.ItemTemplate = (DataTemplate)this.Resources[
            this._isThumbnailView ? "NoteThumbnailItemTemplate" : "NoteListItemTemplate"];
        this.NotesList.ItemsPanel = (ItemsPanelTemplate)this.Resources[
            this._isThumbnailView ? "NoteThumbnailItemsPanel" : "NoteListItemsPanel"];
        this.NotesList.ItemContainerStyle = (Style)this.Resources[
            this._isThumbnailView ? "NoteThumbnailItemContainerStyle" : "NoteListItemContainerStyle"];
        _ = this.DispatcherQueue.TryEnqueue(this.UpdateNoteSelectionIndicators);
        if (this._isThumbnailView)
        {
            this.QueueThumbnailLayoutRefresh();
        }
    }

    private void OnNoteRowPointerEntered(object sender, PointerRoutedEventArgs args)
    {
        if (args.Pointer.PointerDeviceType != PointerDeviceType.Mouse ||
            sender is not FrameworkElement { Tag: NoteLibraryEntry } row)
        {
            return;
        }

        if (this._hoveredNoteRow is { } previousRow && !ReferenceEquals(previousRow, row))
        {
            this.UpdateNoteHoverShadow(previousRow, false);
            this.UpdateNoteSelectionIndicator(previousRow, false);
        }

        this._hoveredNoteRow = row;
        this.UpdateNoteSelectionIndicator(row, true);
        this.UpdateNoteHoverShadow(row, true);
    }

    private void OnNoteRowContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if (sender is FrameworkElement { Tag: NoteLibraryEntry entry } &&
            this.ViewModel.Entries.Contains(entry))
        {
            if (!this.NotesList.SelectedItems.Contains(entry))
            {
                this.NotesList.SelectedItems.Clear();
                this.NotesList.SelectedItems.Add(entry);
            }

        }
    }

    private void OnNoteContextMenuOpening(object sender, object args)
    {
        if (sender is not MenuFlyout { Target.Tag: NoteLibraryEntry entry } flyout ||
            !entry.CanChangeColor ||
            flyout.Items.OfType<MenuFlyoutSubItem>()
                .FirstOrDefault(static item => Equals(item.Tag, "note-color")) is not { } colorMenu)
        {
            return;
        }

        colorMenu.Items.Clear();
        foreach (var color in Enum.GetValues<NoteColor>())
        {
            var item = new RadioMenuFlyoutItem
            {
                Text = color.ToString(),
                GroupName = $"LibraryNoteColor-{entry.Note.Id}",
                IsChecked = entry.Note.Color == color,
                Icon = new FontIcon
                {
                    Glyph = "\uE91F",
                    Foreground = new SolidColorBrush(NotePalette.Resolve(color, false))
                }
            };
            item.Click += async (_, _) => await this.ViewModel.ChangeColorAsync(entry, color);
            colorMenu.Items.Add(item);
        }
    }

    private void OnNoteRowPointerExited(object sender, PointerRoutedEventArgs args)
    {
        if (!ReferenceEquals(this._hoveredNoteRow, sender))
        {
            return;
        }

        this._hoveredNoteRow = null;
        if (sender is FrameworkElement row)
        {
            this.UpdateNoteHoverShadow(row, false);
            this.UpdateNoteSelectionIndicator(row, false);
        }
    }

    private void OnNoteSelectionCheckBoxLoaded(object sender, RoutedEventArgs args)
    {
        if (sender is CheckBox checkBox)
        {
            this.UpdateNoteSelectionCheckBox(
                checkBox,
                ReferenceEquals(this.FindNoteRow(checkBox), this._hoveredNoteRow));
        }
    }

    private void OnNoteSelectionCheckBoxPointerPressed(object sender, PointerRoutedEventArgs args) =>
        args.Handled = true;

    private void OnNoteSelectionCheckBoxClick(object sender, RoutedEventArgs args)
    {
        if (sender is not CheckBox { Tag: NoteLibraryEntry entry } checkBox ||
            !this.ViewModel.Entries.Contains(entry))
        {
            return;
        }

        if (checkBox.IsChecked == true)
        {
            if (!this.NotesList.SelectedItems.Contains(entry))
            {
                this.NotesList.SelectedItems.Add(entry);
            }
        }
        else
        {
            this.NotesList.SelectedItems.Remove(entry);
        }

        this.UpdateNoteSelectionIndicators();
    }

    private void UpdateNoteSelectionIndicators()
    {
        foreach (var checkBox in FindDescendants<CheckBox>(this.NotesList)
                     .Where(static candidate => candidate.Name == "NoteSelectionCheckBox"))
        {
            var row = this.FindNoteRow(checkBox);
            this.UpdateNoteSelectionCheckBox(checkBox, ReferenceEquals(row, this._hoveredNoteRow));
        }
    }

    private void UpdateNoteSelectionIndicator(FrameworkElement row, bool isHovered)
    {
        var checkBox = FindDescendants<CheckBox>(row)
            .FirstOrDefault(static candidate => candidate.Name == "NoteSelectionCheckBox");
        if (checkBox is not null)
        {
            this.UpdateNoteSelectionCheckBox(checkBox, isHovered);
        }
    }

    private void UpdateNoteSelectionCheckBox(CheckBox checkBox, bool isHovered)
    {
        if (checkBox.Tag is not NoteLibraryEntry entry)
        {
            return;
        }

        var isSelected = this.NotesList.SelectedItems.Contains(entry);
        checkBox.IsChecked = isSelected;
        checkBox.Visibility = this.NotesList.SelectedItems.Count > 0 || isHovered
            ? Visibility.Visible
            : Visibility.Collapsed;

        var row = this.FindNoteRow(checkBox);
        var selectionBorder = row is null
            ? null
            : FindDescendants<Border>(row)
                .FirstOrDefault(static candidate => candidate.Name == "NoteSelectionBorder");
        if (selectionBorder is not null)
        {
            selectionBorder.Visibility = isSelected ? Visibility.Visible : Visibility.Collapsed;
        }

        var surface = row is null
            ? null
            : FindDescendants<Border>(row)
                .FirstOrDefault(static candidate => candidate.Name == "NoteCardSurface");
        if (surface is not null)
        {
            surface.Opacity = isSelected || isHovered ? 1 : 0;
        }
    }

    private FrameworkElement? FindNoteRow(DependencyObject child)
    {
        var current = VisualTreeHelper.GetParent(child);
        while (current is not null && current != this.NotesList)
        {
            if (current is FrameworkElement { Tag: NoteLibraryEntry })
            {
                return (FrameworkElement)current;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private void UpdateNoteHoverShadow(DependencyObject row, bool isHovered)
    {
        var container = this.FindNoteContainer(row);
        var surface = FindDescendants<Border>(row)
            .FirstOrDefault(static candidate => candidate.Name == "NoteCardSurface");
        if (container is null || surface is null)
        {
            return;
        }

        var translation = surface.Translation;
        surface.Translation = new Vector3(
            translation.X,
            translation.Y,
            isHovered ? NoteHoverElevation : 0);
        surface.Shadow = isHovered ? new ThemeShadow() : null;
        Canvas.SetZIndex(container, isHovered ? 1 : 0);
    }

    private GridViewItem? FindNoteContainer(DependencyObject child)
    {
        for (var current = child;
             current is not null && current != this.NotesList;
             current = VisualTreeHelper.GetParent(current))
        {
            if (current is GridViewItem container)
            {
                return container;
            }
        }

        return null;
    }

    private static IEnumerable<T> FindDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childCount; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in FindDescendants<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private void OnNotesListSizeChanged(object sender, SizeChangedEventArgs args) =>
        this.UpdateNoteThumbnailSize();

    private void QueueThumbnailLayoutRefresh()
    {
        _ = this.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (!this._isThumbnailView || !this.IsLoaded)
            {
                return;
            }

            // A view-mode change can reuse the same ItemsPanelTemplate. Materialize the current
            // panel first, then apply its new dimensions and consume that invalidation now instead
            // of waiting for a pointer/focus event to trigger another layout pass.
            this.NotesList.InvalidateMeasure();
            this.NotesList.UpdateLayout();
            this.UpdateNoteThumbnailSize();
            this.NotesList.UpdateLayout();
        });
    }

    private void UpdateNoteThumbnailSize()
    {
        if (!this._isThumbnailView ||
            this.NotesList.ItemsPanelRoot is not ItemsWrapGrid itemsPanel ||
            this.NotesList.ActualWidth <= 0)
        {
            return;
        }

        var availableWidth = this.NotesList.ActualWidth -
                             this.NotesList.Padding.Left -
                             this.NotesList.Padding.Right;
        var preferredWidth = this.ViewModel.LayoutMode switch
        {
            OrganizerCollectionViewMode.Small => 160,
            OrganizerCollectionViewMode.Large => 300,
            _ => 220
        };
        var itemWidth = StackThumbnailLayout.GetItemWidth(availableWidth, preferredWidth);
        var layoutChanged = false;
        if (itemWidth > 0 && itemsPanel.ItemWidth != itemWidth)
        {
            itemsPanel.ItemWidth = itemWidth;
            layoutChanged = true;
        }

        var itemHeight = this.ViewModel.LayoutMode switch
        {
            OrganizerCollectionViewMode.Small => 190,
            OrganizerCollectionViewMode.Large => 290,
            _ => 230
        };
        if (itemsPanel.ItemHeight != itemHeight)
        {
            itemsPanel.ItemHeight = itemHeight;
            layoutChanged = true;
        }

        if (layoutChanged)
        {
            itemsPanel.InvalidateMeasure();
            this.NotesList.InvalidateMeasure();
        }
    }

    private async void OnOpenClick(object sender, RoutedEventArgs args)
    {
        await this.ViewModel.OpenSelectionAsync(this.GetSelectedEntries());
    }

    private async void OnOpenTaggedNoteClick(object sender, RoutedEventArgs args)
    {
        if ((sender as FrameworkElement)?.Tag is NoteLibraryEntry entry)
        {
            if (entry.IsDeleted && this.NotesList.SelectedItems.Contains(entry))
            {
                await this.ViewModel.OpenSelectionAsync(this.GetSelectedEntries());
            }
            else
            {
                await this.ViewModel.OpenAsync(entry);
            }
        }
    }

    private void OnGoToSelectedNoteStackClick(object sender, RoutedEventArgs args)
    {
        if (this.GetSelectedEntries() is [var entry])
        {
            GoToNoteStack(entry);
        }
    }

    private void OnGoToTaggedNoteStackClick(object sender, RoutedEventArgs args)
    {
        if ((sender as FrameworkElement)?.Tag is NoteLibraryEntry entry)
        {
            GoToNoteStack(entry);
        }
    }

    private static void GoToNoteStack(NoteLibraryEntry entry)
    {
        if (entry.CanGoToStack)
        {
            App.Current.ShowNoteOwner(entry.Target.StackId, entry.Target.ItemId);
        }
    }

    private async void OnDeleteTaggedNoteClick(object sender, RoutedEventArgs args)
    {
        if ((sender as FrameworkElement)?.Tag is NoteLibraryEntry entry)
        {
            var selected = this.NotesList.SelectedItems.Contains(entry)
                ? this.GetSelectedEntries()
                : [entry];
            await this.DeleteEntriesAsync(selected);
        }
    }

    private void OnNewClick(object sender, RoutedEventArgs args) => App.Current.CreateQuickNote();

    private async void OnClipboardClick(object sender, RoutedEventArgs args) =>
        await App.Current.CreateClipboardNoteAsync();

    private async void OnNotesDoubleTapped(object sender, DoubleTappedRoutedEventArgs args)
    {
        var element = args.OriginalSource as DependencyObject;
        while (element is not null && element != this.NotesList)
        {
            if (element is FrameworkElement { DataContext: NoteLibraryEntry entry })
            {
                args.Handled = true;
                await this.ViewModel.OpenAsync(entry);
                return;
            }

            element = VisualTreeHelper.GetParent(element);
        }
    }

    private async void OnNotesKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key == VirtualKey.Space &&
            args.OriginalSource is not CheckBox &&
            this.GetFocusedNote() is { } focusedNote)
        {
            args.Handled = true;
            if (this.NotesList.SelectedItems.Contains(focusedNote))
            {
                this.NotesList.SelectedItems.Remove(focusedNote);
            }
            else
            {
                this.NotesList.SelectedItems.Add(focusedNote);
            }

            return;
        }

        if (args.Key == VirtualKey.Enter && this.ViewModel.CanOpenSelection)
        {
            args.Handled = true;
            await this.ViewModel.OpenSelectionAsync(this.GetSelectedEntries());
        }
    }

    private NoteLibraryEntry? GetFocusedNote()
    {
        for (var element = FocusManager.GetFocusedElement(this.XamlRoot) as DependencyObject;
             element is not null && !ReferenceEquals(element, this.NotesList);
             element = VisualTreeHelper.GetParent(element))
        {
            if (element is GridViewItem container)
            {
                return this.NotesList.ItemFromContainer(container) as NoteLibraryEntry;
            }
        }

        return this.NotesList.SelectedItem as NoteLibraryEntry ?? this.GetSelectedEntries().FirstOrDefault();
    }

    private async void OnDeleteClick(object sender, RoutedEventArgs args)
    {
        await this.DeleteEntriesAsync(this.GetSelectedEntries());
    }

    private Task DeleteEntriesAsync(IReadOnlyList<NoteLibraryEntry> entries)
    {
        if (entries.Count == 0)
        {
            return Task.CompletedTask;
        }

        var permanently = entries.All(static entry => entry.IsDeleted);
        var plural = entries.Count > 1;
        var title = permanently
            ? plural ? $"Permanently delete {entries.Count} notes?" : "Permanently delete note?"
            : plural ? $"Delete {entries.Count} notes?" : "Delete note?";
        var message = permanently
            ? plural
                ? "This deletes the selected notes and their recovery history. This cannot be undone."
                : "This deletes the note and its recovery history. This cannot be undone."
            : plural
                ? "This moves the selected notes to Recently deleted. Their associated stacks or items are kept."
                : "This moves the note to Recently deleted. The associated stack or item is kept.";
        return this.ViewModel.DeleteAsync(entries, async () =>
        {
            var dialog = new ContentDialog
            {
                XamlRoot = this.RootGrid.XamlRoot,
                Title = title,
                Content = message,
                PrimaryButtonText = permanently ? "Delete permanently" : "Delete",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close
            };
            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        });
    }

    private NoteLibraryEntry[] GetSelectedEntries() =>
        this.NotesList.SelectedItems.OfType<NoteLibraryEntry>().ToArray();

    private void SynchronizeSelectionFromViewModel()
    {
        var selected = this.ViewModel.SelectedEntries
            .Where(this.ViewModel.Entries.Contains)
            .ToArray();
        if (this.NotesList.SelectedItems.OfType<NoteLibraryEntry>().ToHashSet().SetEquals(selected))
        {
            return;
        }

        this._isSynchronizingSelection = true;
        try
        {
            this.NotesList.SelectedItems.Clear();
            foreach (var entry in selected)
            {
                this.NotesList.SelectedItems.Add(entry);
            }
        }
        finally
        {
            this._isSynchronizingSelection = false;
        }
    }

    public void Dispose()
    {
        this.ViewModel.SelectedNoteChanged -= this.OnSelectedNoteChanged;
        this.ViewModel.Dispose();
    }
}
