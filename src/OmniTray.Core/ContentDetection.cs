// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Net;
using System.Text.RegularExpressions;

namespace OmniTray.Core;

public static partial class ContentDetection
{
    public static bool TryNormalizeWebUrl(string? value, out string normalizedUrl)
    {
        normalizedUrl = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var candidate = value.Trim();
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return false;
        }

        normalizedUrl = uri.AbsoluteUri;
        return true;
    }

    public static string? ExtractSourceUrlFromHtml(string? htmlFormat)
    {
        if (string.IsNullOrWhiteSpace(htmlFormat))
        {
            return null;
        }

        var match = SourceUrlHeaderRegex().Match(htmlFormat);
        return match.Success && TryNormalizeWebUrl(match.Groups[1].Value, out var sourceUrl)
            ? sourceUrl
            : null;
    }

    public static string ExtractPlainTextFromHtml(string htmlFormat)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(htmlFormat);

        var fragment = ExtractHtmlFragment(htmlFormat);
        fragment = ScriptAndStyleRegex().Replace(fragment, " ");
        fragment = HtmlTagRegex().Replace(fragment, " ");
        return NormalizeWhitespace(WebUtility.HtmlDecode(fragment));
    }

    public static bool ContainsHtmlTable(string? htmlFormat)
    {
        if (string.IsNullOrWhiteSpace(htmlFormat))
        {
            return false;
        }

        // Excel wraps StartFragment/EndFragment inside the table element, so limiting this
        // check to the fragment itself discards the markup that identifies the payload.
        return HtmlTableRegex().IsMatch(htmlFormat);
    }

    public static bool IsTabular(string? text, string? html, string? rtf) =>
        ContainsHtmlTable(html) ||
        (!string.IsNullOrWhiteSpace(text) && text.Contains('\t')) ||
        (!string.IsNullOrWhiteSpace(rtf) && RtfTableControlWordRegex().IsMatch(rtf));

    public static bool IsCode(string? text, string? html) =>
        (!string.IsNullOrWhiteSpace(text) && MarkdownCodeFenceRegex().IsMatch(text)) ||
        (!string.IsNullOrWhiteSpace(html) && HtmlCodeElementRegex().IsMatch(html));

    public static bool IsEmail(string? text, string? applicationLink) =>
        (!string.IsNullOrWhiteSpace(applicationLink) &&
         Uri.TryCreate(applicationLink.Trim(), UriKind.Absolute, out var uri) &&
         string.Equals(uri.Scheme, "mailto", StringComparison.OrdinalIgnoreCase)) ||
        (!string.IsNullOrWhiteSpace(text) && EmailAddressRegex().IsMatch(text.Trim()));

    public static bool IsColor(string? text) =>
        !string.IsNullOrWhiteSpace(text) && CssColorRegex().IsMatch(text.Trim());

    public static string CreateUrlDisplayName(string normalizedUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedUrl);
        if (!Uri.TryCreate(normalizedUrl, UriKind.Absolute, out var uri))
        {
            return normalizedUrl;
        }

        return uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
            ? uri.Host[4..]
            : uri.Host;
    }

    private static string ExtractHtmlFragment(string htmlFormat)
    {
        const string startMarker = "<!--StartFragment-->";
        const string endMarker = "<!--EndFragment-->";
        var start = htmlFormat.IndexOf(startMarker, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return htmlFormat;
        }

        start += startMarker.Length;
        var end = htmlFormat.IndexOf(endMarker, start, StringComparison.OrdinalIgnoreCase);
        return end < 0 ? htmlFormat[start..] : htmlFormat[start..end];
    }

    private static string NormalizeWhitespace(string value) => string.Join(
        ' ',
        value.Split(default(char[]), StringSplitOptions.RemoveEmptyEntries));

    [GeneratedRegex(@"(?im)^SourceURL:\s*(\S+)\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex SourceUrlHeaderRegex();

    [GeneratedRegex(@"(?is)<(?:script|style)\b[^>]*>.*?</(?:script|style)\s*>", RegexOptions.CultureInvariant)]
    private static partial Regex ScriptAndStyleRegex();

    [GeneratedRegex(@"(?s)<[^>]+>", RegexOptions.CultureInvariant)]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex(@"(?i)<table(?:\s|>)", RegexOptions.CultureInvariant)]
    private static partial Regex HtmlTableRegex();

    [GeneratedRegex(@"\\(?:trowd|cellx\d+|cell)\b", RegexOptions.CultureInvariant)]
    private static partial Regex RtfTableControlWordRegex();

    [GeneratedRegex(@"(?m)^\s*```", RegexOptions.CultureInvariant)]
    private static partial Regex MarkdownCodeFenceRegex();

    [GeneratedRegex(@"(?i)<(?:pre|code)(?:\s|>)", RegexOptions.CultureInvariant)]
    private static partial Regex HtmlCodeElementRegex();

    [GeneratedRegex(@"^[^\s@]+@[^\s@]+\.[^\s@]+$", RegexOptions.CultureInvariant)]
    private static partial Regex EmailAddressRegex();

    [GeneratedRegex(@"(?i)^(?:#[0-9a-f]{3,8}|(?:rgb|rgba|hsl|hsla)\s*\([^\r\n]+\))$", RegexOptions.CultureInvariant)]
    private static partial Regex CssColorRegex();
}
