// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace OmniTray.Controls;

public sealed partial class NoteTextEditor : UserControl
{
    public event EventHandler? TextChanged;
    private string? _contentRtf;
    private string? _contentText;
    private bool _focusRefreshQueued;
    private Exception? _formattingError;
    private bool _hostActive = true;
    private bool _isLoading;
    private string? _lastDocumentRtf;
    private Guid? _noteId;

    internal RichEditTextDocument TextDocument => this.TextBox.TextDocument;

    internal bool HasEditingFocus
    {
        get
        {
            // Overflow commands live in a popup outside this control's visual tree.
            if (this.FormattingBar.IsOpen) { return true; }

            if (this.XamlRoot is not { } root) { return false; }

            for (var element = FocusManager.GetFocusedElement(root) as DependencyObject;
                 element is not null;
                 element = VisualTreeHelper.GetParent(element))
            {
                if (ReferenceEquals(element, this)) { return true; }
            }

            return false;
        }
    }

    public NoteTextEditor() => this.InitializeComponent();

    internal Exception? SetNote(StickyNote? note)
    {
        if (this._noteId == note?.Id && this._contentText == note?.Text && this._contentRtf == note?.Rtf)
        {
            // Catalog echoes and color changes must not reset selection or local undo history.
            return this._formattingError;
        }

        var sameNote = note is not null && this._noteId == note.Id;
        var selection = this.TextDocument.Selection;
        var start = sameNote ? selection.StartPosition : 0;
        var end = sameNote ? selection.EndPosition : 0;
        var wasReadOnly = this.TextBox.IsReadOnly;
        this._isLoading = true;
        this._formattingError = null;
        try
        {
            // An empty or frozen preview is read-only. RichEdit rejects programmatic
            // document replacement in that state too, including the plain-text fallback.
            this.TextBox.IsReadOnly = false;
            try
            {
                this.TextDocument.SetText(
                    string.IsNullOrEmpty(note?.Rtf) ? TextSetOptions.None : TextSetOptions.FormatRtf,
                    string.IsNullOrEmpty(note?.Rtf) ? note?.Text ?? string.Empty : note.Rtf);
            }
            catch (Exception exception)
            {
                this.TextDocument.SetText(TextSetOptions.None, note?.Text ?? string.Empty);
                this._formattingError = exception;
            }

            // Neither a previous note nor a stale version from another editor belongs in Undo.
            this.TextDocument.ClearUndoRedoHistory();
            this.TextDocument.GetText(TextGetOptions.None, out var text);
            var length = Math.Max(0, text.Length - 1);
            selection.SetRange(Math.Min(start, length), Math.Min(end, length));
            this.TextDocument.GetText(TextGetOptions.FormatRtf, out this._lastDocumentRtf);
            this._noteId = note?.Id;
            this._contentText = note?.Text;
            this._contentRtf = note?.Rtf;
        }
        finally
        {
            try { this.TextBox.IsReadOnly = wasReadOnly; }
            finally { this._isLoading = false; }
        }

        this.UpdateFormattingButtons();
        return this._formattingError;
    }

    internal (string Text, string Rtf) ReadContent()
    {
        this.TextDocument.GetText(TextGetOptions.None, out var text);
        this.TextDocument.GetText(TextGetOptions.FormatRtf, out var rtf);
        // Remove only RichEdit's final paragraph marker, retaining user-entered blank lines.
        if (text.EndsWith('\r')) { text = text[..^1]; }

        this._contentText = text;
        this._contentRtf = this._lastDocumentRtf = rtf;
        this._formattingError = null;
        return (text, rtf);
    }

    internal void FocusText(FocusState state) => this.TextBox.Focus(state);

    internal void SetEditingEnabled(bool enabled)
    {
        this.TextBox.IsReadOnly = !enabled;
        this.FormattingBar.IsEnabled = enabled;
    }

    internal void SetHostActive(bool active)
    {
        this._hostActive = active;
        if (!active) { this.FormattingBar.IsOpen = false; }

        this.QueueFocusRefresh();
    }

    internal void SetBackgroundBrush(Brush brush)
    {
        this.RootGrid.Background = brush;
        foreach (var key in new[]
                 {
                     "TextControlBackground", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused"
                 })
        {
            this.TextBox.Resources[key] = brush;
        }

        this.TextBox.Background = brush;
    }

    private void OnTextChanged(object sender, RoutedEventArgs args) => this.NotifyEdit();

    private void NotifyEdit()
    {
        if (this._isLoading || this._noteId is null) { return; }

        this.TextDocument.GetText(TextGetOptions.FormatRtf, out var rtf);
        if (rtf == this._lastDocumentRtf) { return; }

        this.ReadContent();
        this.TextChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnFocusChanged(object sender, RoutedEventArgs args) => this.QueueFocusRefresh();

    private void OnFormattingBarOpenChanged(object sender, object args) => this.QueueFocusRefresh();

    private void QueueFocusRefresh()
    {
        if (this._focusRefreshQueued) { return; }

        this._focusRefreshQueued = this.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            this._focusRefreshQueued = false;
            // Let focus settle before hiding controls a user is clicking.
            this.FormattingBar.Visibility = this._hostActive && this.HasEditingFocus
                ? Visibility.Visible
                : Visibility.Collapsed;
        });
    }

    private void OnSelectionChanged(object sender, RoutedEventArgs args) => this.UpdateFormattingButtons();

    private void UpdateFormattingButtons()
    {
        if (this._isLoading || this.BoldButton is null) { return; }

        var selection = this.TextDocument.Selection;
        this.BoldButton.IsChecked = selection.CharacterFormat.Bold == FormatEffect.On;
        this.ItalicButton.IsChecked = selection.CharacterFormat.Italic == FormatEffect.On;
        this.UnderlineButton.IsChecked = selection.CharacterFormat.Underline == UnderlineType.Single;
        this.BulletsButton.IsChecked = selection.ParagraphFormat.ListType == MarkerType.Bullet;
    }

    private void OnBoldClick(object sender, RoutedEventArgs args) =>
        this.FormatSelection(format => format.Bold = FormatEffect.Toggle);

    private void OnItalicClick(object sender, RoutedEventArgs args) =>
        this.FormatSelection(format => format.Italic = FormatEffect.Toggle);

    private void OnUnderlineClick(object sender, RoutedEventArgs args) =>
        this.FormatSelection(format => format.Underline = format.Underline == UnderlineType.None
            ? UnderlineType.Single
            : UnderlineType.None);

    private void FormatSelection(Action<ITextCharacterFormat> apply)
    {
        var selection = this.TextDocument.Selection;
        var format = selection.CharacterFormat;
        apply(format);
        selection.CharacterFormat = format;
        this.FormattingBar.IsOpen = false;
        this.FocusText(FocusState.Programmatic);
        this.NotifyEdit();
        this.UpdateFormattingButtons();
    }

    private void OnBulletsClick(object sender, RoutedEventArgs args)
    {
        var selection = this.TextDocument.Selection;
        var format = selection.ParagraphFormat;
        format.ListType = format.ListType == MarkerType.Bullet ? MarkerType.None : MarkerType.Bullet;
        selection.ParagraphFormat = format;
        this.FormattingBar.IsOpen = false;
        this.FocusText(FocusState.Programmatic);
        this.NotifyEdit();
        this.UpdateFormattingButtons();
    }
}
