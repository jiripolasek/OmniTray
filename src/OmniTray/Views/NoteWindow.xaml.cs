// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Media;

namespace OmniTray.Views;

public sealed partial class NoteWindow : Window
{
    private readonly MainViewModel _catalog;
    private readonly DispatcherQueueTimer _saveTimer;
    private StickyNote _note;
    private bool _isLoading = true;
    private bool _isClosing;
    private bool _allowClose;
    private bool _isClosed;
    private bool _isDeleting;
    private string? _lastEditorRtf;
    private (NoteColor Color, ElementTheme Theme, bool HighContrast)? _appliedColor;
    private int _editVersion;

    internal NoteWindow(MainViewModel catalog, StickyNote note)
    {
        this._catalog = catalog;
        this._note = note;
        this.InitializeComponent();
        this.InitializeChrome();
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "OmniTray.ico");
        if (File.Exists(iconPath))
        {
            this.AppWindow.SetIcon(iconPath);
        }
        this._saveTimer = this.DispatcherQueue.CreateTimer();
        this._saveTimer.Interval = TimeSpan.FromMilliseconds(500);
        this._saveTimer.IsRepeating = false;
        this._saveTimer.Tick += this.OnSaveTimerTick;
        this.AppWindow.Closing += this.OnClosing;
        this.Closed += this.OnClosed;
        this._catalog.CatalogChanged += this.OnCatalogChanged;
        App.Current.SystemColorsChanged += this.OnSystemColorsChanged;
        if (this.AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsMaximizable = false;
            presenter.PreferredMinimumWidth = 320;
            presenter.PreferredMinimumHeight = 300;
        }
    }

    internal Guid NoteId => this._note.Id;

    internal void CloseDeleted()
    {
        this._allowClose = true;
        this.Close();
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (!this._isLoading)
        {
            return;
        }

        if (this.Editor.SetNote(this._note) is { } exception)
        {
            this.ShowError("The saved formatting could not be opened. Plain text is available; the original RTF is kept until you edit.", exception, "Could not open formatting");
        }

        this._isLoading = false;
        this.RootGrid.XamlRoot.Changed += this.OnXamlRootChanged;
        this.UpdateTitleBarInput();
        this.Editor.TextDocument.GetText(TextGetOptions.FormatRtf, out this._lastEditorRtf);
        this.RefreshPresentation();
        this.Editor.FocusText(FocusState.Programmatic);
        // Also persist a newly created blank note and surface initial write failures.
        this.ScheduleSave();
    }

    private void OnTextChanged(object? sender, EventArgs args) => this.CaptureEdit();

    private void CaptureEdit()
    {
        if (this._isLoading || this._isClosed || this._isClosing || this._isDeleting || this._catalog.FindNote(this.NoteId) is null)
        {
            return;
        }

        var (text, rtf) = this.Editor.ReadContent();
        if (rtf == this._lastEditorRtf)
        {
            return;
        }

        this._lastEditorRtf = rtf;
        this._catalog.UpdateNote(this.NoteId, text, rtf, this._note.Color);
        this.ScheduleSave();
    }

    private void ScheduleSave()
    {
        this._editVersion++;
        this.SaveStatusText.Text = "Saving…";
        this.UpdateDetailsToolTip();
        this._saveTimer.Stop();
        this._saveTimer.Start();
    }

    private async void OnSaveTimerTick(DispatcherQueueTimer sender, object args) => await this.SaveAsync();

    private async void OnSaveClick(object sender, RoutedEventArgs args) => await this.SaveAsync();

    private async Task<bool> SaveAsync()
    {
        var version = this._editVersion;
        try
        {
            await App.Current.SaveNotesAsync();
            if (!this._isClosed && version == this._editVersion)
            {
                this.SaveStatusText.Text = "Saved";
                this.UpdateDetailsToolTip();
                this.ErrorBar.IsOpen = false;
            }

            if (this._isDeleting && !this._isClosed && version == this._editVersion)
            {
                this.CloseDeleted();
            }

            return true;
        }
        catch (Exception exception)
        {
            if (!this._isClosed)
            {
                this.SaveStatusText.Text = "Not saved";
                this.UpdateDetailsToolTip();
                this.ShowError("Changes are still in memory. Try saving again before closing.", exception);
            }

            return false;
        }
    }

    private async void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (this._allowClose)
        {
            return;
        }

        args.Cancel = true;
        await this.CloseAfterSavingAsync();
    }

    private async Task CloseAfterSavingAsync()
    {
        if (this._isClosing || this._isClosed)
        {
            return;
        }

        this._isClosing = true;
        this._saveTimer.Stop();
        this.SetEditingEnabled(false);
        if (await this.SaveAsync())
        {
            if (!this._isClosed)
            {
                this._allowClose = true;
                this.Close();
            }
        }
        else if (!this._isClosed)
        {
            this._isClosing = false;
            this.SetEditingEnabled(true);
        }
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        this._isClosed = true;
        this.UninitializeChrome();
        this._saveTimer.Stop();
        this._saveTimer.Tick -= this.OnSaveTimerTick;
        this._catalog.CatalogChanged -= this.OnCatalogChanged;
        App.Current.SystemColorsChanged -= this.OnSystemColorsChanged;
        this.AppWindow.Closing -= this.OnClosing;
        this.Closed -= this.OnClosed;
    }

    private void OnCatalogChanged(object? sender, EventArgs args)
    {
        if (this._catalog.FindNote(this.NoteId) is not { } location)
        {
            if (this._isDeleting)
            {
                return;
            }

            // Defer destruction until catalog notifications and any open flyout have unwound.
            this.DispatcherQueue.TryEnqueue(() =>
            {
                if (!this._isClosed && this._catalog.FindNote(this.NoteId) is null)
                {
                    this.CloseDeleted();
                }
            });
            return;
        }

        this._note = location.Note;
        if (!this._isLoading)
        {
            if (this.Editor.SetNote(this._note) is { } exception)
            {
                this.ShowError("The saved formatting could not be opened. Plain text is available; the original RTF is kept until you edit.", exception, "Could not open formatting");
            }
            this.Editor.TextDocument.GetText(TextGetOptions.FormatRtf, out this._lastEditorRtf);
        }
        this.RefreshPresentation();
    }

    private void RefreshPresentation()
    {
        this.Title = $"{this._note.DisplayName} — OmniTray Note";
        this.UpdatedText.Text = $"Updated {this._note.UpdatedAt.ToLocalTime():g}";
        this.CreatedText.Text = $"Created {this._note.CreatedAt.ToLocalTime():g}";
        this.UpdateDetailsToolTip();
        var location = this._catalog.FindNote(this.NoteId);
        var stack = this._catalog.Stacks.FirstOrDefault(stack => stack.Model.Id == location?.Target.StackId);
        var item = stack?.Model.Items.FirstOrDefault(item => item.Id == location?.Target.ItemId);
        this.LocationText.Text = location?.Target.Placement switch
        {
            NotePlacement.Item => $"Attached to: {item?.DisplayName} · {stack?.Name}",
            _ => $"In stack: {stack?.Name}"
        };
        ToolTipService.SetToolTip(this.LocationText, this.LocationText.Text);
        this.DetachButton.Visibility = location?.Target.Placement == NotePlacement.StackItem
            ? Visibility.Collapsed : Visibility.Visible;
        var history = this._catalog.NoteHistory.FirstOrDefault(entry => entry.NoteId == this.NoteId);
        this.InspectSourceButton.Visibility = history is null ? Visibility.Collapsed : Visibility.Visible;
        var sourceExists = history is not null && this._catalog.Stacks.Any(stack => stack.Model.Items.Any(item =>
            item.Id == history.SourceItem.Id && item.Kind != DropItemKind.Note));
        this.ShowSourceButton.Visibility = sourceExists ? Visibility.Visible : Visibility.Collapsed;
        this.UndoConversionButton.Visibility = history?.IsConversion == true && !sourceExists
            ? Visibility.Visible : Visibility.Collapsed;
        this.ApplyColor();
    }

    private void OnThemeChanged(FrameworkElement sender, object args) => this.ApplyColor();

    private void OnSystemColorsChanged(object? sender, EventArgs args)
    {
        this._appliedColor = null;
        if (!this._isClosed) { this.ApplyColor(); }
    }

    private void ApplyColor()
    {
        if (this.RootGrid is null || this.Editor is null)
        {
            return;
        }

        var appearance = (this._note.Color, this.RootGrid.ActualTheme, App.Current.IsHighContrast);
        if (this._appliedColor == appearance)
        {
            return;
        }

        this._appliedColor = appearance;
        var dark = this.RootGrid.ActualTheme == ElementTheme.Dark;
        var background = App.Current.IsHighContrast
            ? (SolidColorBrush)Application.Current.Resources["ApplicationPageBackgroundThemeBrush"]
            : new SolidColorBrush(NotePalette.Resolve(this._note.Color, dark));
        this.RootGrid.Background = background;
        this.Editor.SetBackgroundBrush(background);
        this.UpdateHeaderActions();
    }

    private void OnColorMenuOpening(object sender, object args)
    {
        this.ColorMenu.Items.Clear();
        foreach (var color in Enum.GetValues<NoteColor>())
        {
            var item = new RadioMenuFlyoutItem
            {
                Text = color.ToString(),
                GroupName = "NoteColor",
                IsChecked = this._note.Color == color,
                Icon = new FontIcon { Glyph = "\uE91F", Foreground = new SolidColorBrush(NotePalette.Resolve(color, false)) }
            };
            item.Click += (_, _) =>
            {
                this._catalog.UpdateNote(this.NoteId, this._note.Text, this._note.Rtf, color);
                this.ScheduleSave();
            };
            this.ColorMenu.Items.Add(item);
        }
    }

    private async void OnMoveClick(object sender, RoutedEventArgs args)
    {
        await NotePlacementDialog.ShowAsync(this, this._catalog, this.NoteId);
        if (!this._isClosed)
        {
            this.ScheduleSave();
        }
    }

    private void OnDetachClick(object sender, RoutedEventArgs args)
    {
        if (this._catalog.FindNote(this.NoteId) is { } location)
        {
            this._catalog.MoveNote(this.NoteId, new NoteTarget(location.Target.StackId, NotePlacement.StackItem));
            this.ScheduleSave();
        }
    }

    private void OnKeepOnTopClick(object sender, RoutedEventArgs args)
    {
        if (this.AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = this.KeepOnTopButton.IsChecked == true;
        }
    }

    private async void OnDeleteClick(object sender, RoutedEventArgs args)
    {
        if (await StackDialogWindow.ShowAsync(this, "Delete note?",
                "This moves the note to Recently deleted. You can restore it from Browse notes. The associated stack or item is kept.", "Delete"))
        {
            this._isDeleting = true;
            this._editVersion++;
            this._saveTimer.Stop();
            this.SetEditingEnabled(false);
            this._catalog.DeleteNote(this.NoteId);
            await this.SaveAsync();
        }
    }

    private void OnLocationClick(object sender, RoutedEventArgs args)
    {
        if (this._catalog.FindNote(this.NoteId) is { } location)
        {
            App.Current.ShowNoteOwner(location.Target.StackId,
                location.Target.Placement == NotePlacement.StackItem ? this.NoteId : location.Target.ItemId);
        }
    }

    private void OnShowSourceClick(object sender, RoutedEventArgs args)
    {
        if (this._catalog.NoteHistory.FirstOrDefault(entry => entry.NoteId == this.NoteId) is { } history &&
            this._catalog.Stacks.FirstOrDefault(stack => stack.Model.Items.Any(item =>
                item.Id == history.SourceItem.Id && item.Kind != DropItemKind.Note)) is { } stack)
        {
            App.Current.ShowNoteOwner(stack.Model.Id, history.SourceItem.Id);
        }
    }

    private void OnInspectSourceClick(object sender, RoutedEventArgs args)
    {
        if (this._catalog.NoteHistory.FirstOrDefault(entry => entry.NoteId == this.NoteId) is { } history)
        {
            App.Current.ShowDataFormatInspector(history.SourceItem);
        }
    }

    private async void OnUndoConversionClick(object sender, RoutedEventArgs args)
    {
        if (!await StackDialogWindow.ShowAsync(this, "Undo conversion?",
            "Restore the original capture in this stack. The edited note is kept in Recently deleted. Annotations moved elsewhere stay where they are.", "Restore capture")) { return; }
        try
        {
            this._isDeleting = true;
            this._editVersion++;
            this._saveTimer.Stop();
            this.SetEditingEnabled(false);
            this._catalog.UndoNoteConversion(this.NoteId);
            await this.SaveAsync();
        }
        catch (Exception exception)
        {
            this._isDeleting = false;
            this.SetEditingEnabled(true);
            this.ShowError("The conversion could not be undone.", exception, "Could not undo conversion");
        }
    }

    private async void OnAppendClipboardClick(object sender, RoutedEventArgs args) => await this.AppendClipboardAsync(false);
    private async void OnAppendClipboardWithTimeClick(object sender, RoutedEventArgs args) => await this.AppendClipboardAsync(true);

    private async Task AppendClipboardAsync(bool timestamp)
    {
        try
        {
            var content = await NoteClipboardService.ReadAsync();
            if (this._isLoading || this._isClosing || this._isClosed || this._isDeleting) { return; }
            this.Editor.TextDocument.GetText(TextGetOptions.FormatRtf, out var original);
            this._isLoading = true;
            var undoGroupStarted = false;
            try
            {
                this.Editor.TextDocument.BeginUndoGroup();
                undoGroupStarted = true;
                var selection = this.Editor.TextDocument.Selection;
                selection.EndKey(TextRangeUnit.Story, false);
                var prefix = string.IsNullOrEmpty(this._note.Text) ? "" : "\r\r";
                if (timestamp) { prefix += $"— {DateTimeOffset.Now:g} —\r"; }
                selection.TypeText(prefix);
                selection.SetText(string.IsNullOrEmpty(content.Rtf) ? TextSetOptions.None : TextSetOptions.FormatRtf,
                    content.Rtf ?? content.Text);
            }
            catch
            {
                this.Editor.TextDocument.SetText(TextSetOptions.FormatRtf, original);
                throw;
            }
            finally
            {
                try
                {
                    if (undoGroupStarted) { this.Editor.TextDocument.EndUndoGroup(); }
                }
                finally { this._isLoading = false; }
            }
            this.CaptureEdit();
            this.Editor.FocusText(FocusState.Programmatic);
        }
        catch (Exception exception) { this.ShowError("Clipboard text could not be appended.", exception, "Could not append clipboard"); }
    }

    internal void SetEditingEnabled(bool enabled)
    {
        var canEdit = enabled && !this._isDeleting && !this._isClosing;
        this.Editor.SetEditingEnabled(canEdit);
        this.NoteActionsButton.IsEnabled = canEdit;
    }

    private void ShowError(string message, Exception exception, string title = "Could not save note")
    {
        this.ErrorBar.Title = title;
        this.ErrorBar.Message = $"{message} {exception.Message}";
        this.ErrorBar.IsOpen = true;
    }
}
