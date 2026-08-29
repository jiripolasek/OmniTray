// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

using Windows.ApplicationModel.DataTransfer;
using Microsoft.UI.Text;

namespace OmniTray.Services;

internal sealed record NoteClipboardContent(string Text, string? Rtf);

internal static class NoteClipboardService
{
    public static async Task<NoteClipboardContent> ReadAsync()
    {
        // Read only when explicitly requested; do not monitor or change the clipboard.
        var data = Clipboard.GetContent();
        var text = data.Contains(StandardDataFormats.Text) ? await data.GetTextAsync() : null;
        var rtf = data.Contains(StandardDataFormats.Rtf) ? await data.GetRtfAsync() : null;
        if (!string.IsNullOrWhiteSpace(rtf))
        {
            var decoded = ReadRtfText(rtf);
            return new NoteClipboardContent(text ?? decoded, rtf);
        }

        if (text is null && data.Contains(StandardDataFormats.Html))
        {
            text = ContentDetection.ExtractPlainTextFromHtml(await data.GetHtmlFormatAsync());
        }

        if (string.IsNullOrEmpty(text))
        {
            throw new InvalidOperationException("The clipboard does not contain text or rich text.");
        }

        return new NoteClipboardContent(text, null);
    }

    internal static string ReadRtfText(string rtf)
    {
        var reader = new RichEditBox();
        reader.TextDocument.SetText(TextSetOptions.FormatRtf, rtf);
        reader.TextDocument.GetText(TextGetOptions.UseCrlf | TextGetOptions.AllowFinalEop, out var text);
        return text.EndsWith("\r\n", StringComparison.Ordinal) ? text[..^2] : text;
    }
}
