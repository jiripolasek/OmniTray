// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;

namespace OmniTray.Controls;

public sealed partial class NoteEditorPane : UserControl, IDisposable
{
    private readonly DispatcherQueueTimer _saveTimer;
    private readonly NoteSaveSession _saveSession = new(() => App.Current.SaveNotesAsync());
    private MainViewModel? _catalog;
    private StickyNote? _note;
    private bool _editingEnabled = true;
    private bool _disposed;
    private (NoteColor Color, ElementTheme Theme, bool HighContrast)? _appliedColor;

    public NoteEditorPane()
    {
        this.InitializeComponent();
        this._saveTimer = this.DispatcherQueue.CreateTimer();
        this._saveTimer.Interval = TimeSpan.FromMilliseconds(500);
        this._saveTimer.IsRepeating = false;
        this._saveTimer.Tick += this.OnSaveTimerTick;
    }

    internal event EventHandler? SaveStateChanged;
    internal Guid? NoteId => this._note?.Id;
    internal bool HasUnsavedChanges => this._saveSession.HasUnsavedChanges;
    internal Exception? LastSaveError => this._saveSession.LastError;
    internal bool HasEditingFocus => this.Editor.HasEditingFocus;

    internal void Initialize(MainViewModel catalog)
    {
        this._catalog = catalog;
        catalog.CatalogChanged += this.OnCatalogChanged;
        App.Current.SystemColorsChanged += this.OnSystemColorsChanged;
    }

    internal void SetNote(Guid? noteId)
    {
        if (this._disposed) { return; }
        this._note = noteId is { } id ? this._catalog?.FindNote(id)?.Note : null;
        this.FormatErrorBar.IsOpen = this.Editor.SetNote(this._note) is not null;
        this.Editor.SetEditingEnabled(this._editingEnabled && this._note is not null);
        this.RefreshPresentation();
        // Pending saves deliberately outlive selection and navigation changes.
    }

    internal void FocusText() => this.Editor.FocusText(FocusState.Keyboard);

    internal void SetHostActive(bool active) => this.Editor.SetHostActive(active);

    internal void SetEditingEnabled(bool enabled)
    {
        this._editingEnabled = enabled;
        this.Editor.SetEditingEnabled(enabled && this._note is not null);
        this.ColorButton.IsEnabled = enabled;
    }

    private void OnTextChanged(object? sender, EventArgs args)
    {
        if (this._disposed || !this._editingEnabled || this._note is not { } note || this._catalog?.FindNote(note.Id) is not { } location)
        {
            return;
        }
        var (text, rtf) = this.Editor.ReadContent();
        this._catalog.UpdateNote(note.Id, text, rtf, location.Note.Color);
        this.FormatErrorBar.IsOpen = false;
        this.ScheduleSave();
    }

    private void OnCatalogChanged(object? sender, EventArgs args) => this.SetNote(this.NoteId);

    private void ScheduleSave()
    {
        this._saveSession.MarkChanged();
        this._saveTimer.Stop();
        this._saveTimer.Start();
        this.UpdateSaveState();
    }

    private async void OnSaveTimerTick(DispatcherQueueTimer sender, object args) => await this.SaveAsync();

    internal async Task<bool> SaveAsync()
    {
        this._saveTimer.Stop();
        var saved = await this._saveSession.FlushAsync();
        if (!this._disposed) { this.UpdateSaveState(); }
        return saved;
    }

    private void UpdateSaveState()
    {
        this.SaveStatusText.Text = this.LastSaveError is not null ? "Not saved — retry below"
            : this.HasUnsavedChanges ? "Saving…" : "Changes save automatically";
        this.UpdateDetailsToolTip();
        this.SaveStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshPresentation()
    {
        if (this._note is { } note)
        {
            this.UpdatedText.Text = $"Updated {note.UpdatedAt.ToLocalTime():g}";
            this.CreatedText.Text = $"Created {note.CreatedAt.ToLocalTime():g}";
            this.ApplyColor();
        }
        this.UpdateSaveState();
    }

    private void UpdateDetailsToolTip()
    {
        var details = $"{this.UpdatedText.Text}\n{this.SaveStatusText.Text}";
        ToolTipService.SetToolTip(this.DetailsButton, details);
        AutomationProperties.SetHelpText(this.DetailsButton, details);
    }

    private void OnThemeChanged(FrameworkElement sender, object args) => this.ApplyColor();

    private void OnSystemColorsChanged(object? sender, EventArgs args)
    {
        this._appliedColor = null;
        if (!this._disposed) { this.ApplyColor(); }
    }

    private void ApplyColor()
    {
        if (this._disposed || this._note is not { } note || this.Editor is null) { return; }
        var appearance = (note.Color, this.RootGrid.ActualTheme, App.Current.IsHighContrast);
        if (this._appliedColor == appearance) { return; }
        this._appliedColor = appearance;
        var background = App.Current.IsHighContrast
            ? (SolidColorBrush)Application.Current.Resources["ApplicationPageBackgroundThemeBrush"]
            : new SolidColorBrush(NotePalette.Resolve(note.Color, this.RootGrid.ActualTheme == ElementTheme.Dark));
        this.RootGrid.Background = background;
        this.Editor.SetBackgroundBrush(background);
    }

    private void OnOpenWindowClick(object sender, RoutedEventArgs args)
    {
        if (this.NoteId is { } id) { App.Current.ShowNote(id); }
    }

    private void OnColorMenuOpening(object sender, object args)
    {
        var menu = (MenuFlyout)sender;
        menu.Items.Clear();
        if (this._note is not { } note) { return; }
        foreach (var color in Enum.GetValues<NoteColor>())
        {
            var item = new RadioMenuFlyoutItem
            {
                Text = color.ToString(),
                GroupName = "PreviewNoteColor",
                IsChecked = note.Color == color,
                Icon = new FontIcon { Glyph = "\uE91F", Foreground = new SolidColorBrush(NotePalette.Resolve(color, false)) }
            };
            item.Click += (_, _) =>
            {
                if (!this._disposed && this._editingEnabled && this.NoteId == note.Id && this._catalog?.FindNote(note.Id) is { } location)
                {
                    this._catalog.UpdateNote(note.Id, location.Note.Text, location.Note.Rtf, color);
                    this.ScheduleSave();
                }
            };
            menu.Items.Add(item);
        }
    }

    public void Dispose()
    {
        if (this._disposed) { return; }
        this._disposed = true;
        this._saveTimer.Stop();
        this._saveTimer.Tick -= this.OnSaveTimerTick;
        if (this._catalog is not null) { this._catalog.CatalogChanged -= this.OnCatalogChanged; }
        App.Current.SystemColorsChanged -= this.OnSystemColorsChanged;
    }
}
