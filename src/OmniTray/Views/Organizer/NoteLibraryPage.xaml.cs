// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using Microsoft.UI.Xaml.Input;
using OmniTray.ViewModels.Organizer;
using Windows.System;

namespace OmniTray.Views.Organizer;

public sealed partial class NoteLibraryPage : Page, IDisposable
{
    private readonly Window _owner;

    internal NoteLibraryPage(MainViewModel catalog, Window owner)
    {
        this._owner = owner;
        this.ViewModel = new(catalog);
        this.InitializeComponent();
        this.ViewModel.SelectedNoteChanged += this.OnSelectedNoteChanged;
    }

    public NoteLibraryViewModel ViewModel { get; }
    internal event EventHandler? SelectedNoteChanged;
    internal Guid? SelectedNoteId => this.ViewModel.SelectedNoteId;
    internal void SetActive(bool active) => this.ViewModel.SetActive(active);
    internal void FocusList() => this.NotesList.Focus(FocusState.Keyboard);
    internal void ShowDeleted(bool deleted) => this.ModeBox.SelectedIndex = deleted ? 1 : 0;
    private void OnModeChanged(object sender, SelectionChangedEventArgs args) => this.ViewModel.ShowDeleted = ((ComboBox)sender).SelectedIndex == 1;
    private void OnSearchChanged(object sender, TextChangedEventArgs args) => this.ViewModel.FilterText = ((TextBox)sender).Text;
    private void OnSelectionChanged(object sender, SelectionChangedEventArgs args) => this.ViewModel.SetSelection(this.NotesList.SelectedItem as NoteLibraryEntry);
    private void OnSelectedNoteChanged(object? sender, EventArgs args) => this.SelectedNoteChanged?.Invoke(this, args);

    private async void OnOpenClick(object sender, RoutedEventArgs args)
    {
        if (this.ViewModel.SelectedEntry is { } entry) { await this.ViewModel.OpenAsync(entry); }
    }

    private void OnNewClick(object sender, RoutedEventArgs args) => App.Current.CreateQuickNote();

    private async void OnClipboardClick(object sender, RoutedEventArgs args) => await App.Current.CreateClipboardNoteAsync();

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
            element = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(element);
        }
    }

    private async void OnNotesKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key == VirtualKey.Enter && this.NotesList.SelectedItem is NoteLibraryEntry entry)
        {
            args.Handled = true;
            await this.ViewModel.OpenAsync(entry);
        }
    }

    private async void OnPurgeClick(object sender, RoutedEventArgs args)
    {
        if (this.ViewModel.SelectedEntry is not { IsDeleted: true } entry) { return; }
        await this.ViewModel.PurgeAsync(entry, () => StackDialogWindow.ShowAsync(this._owner, "Permanently delete note?",
            "This deletes the note and its recovery history. This cannot be undone.", "Delete permanently"));
    }

    public void Dispose()
    {
        this.ViewModel.SelectedNoteChanged -= this.OnSelectedNoteChanged;
        this.ViewModel.Dispose();
    }
}
