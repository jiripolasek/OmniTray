// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Net;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;

namespace OmniTray.Core;

public static partial class ContentDetection
{
    private static readonly HashSet<string> VideoFileExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".3g2",
        ".3gp",
        ".avi",
        ".m2ts",
        ".m4v",
        ".mkv",
        ".mov",
        ".mp4",
        ".mpeg",
        ".mpg",
        ".mts",
        ".ts",
        ".webm",
        ".wmv"
    };

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

    public static bool TryNormalizeApplicationLink(string? value, out string normalizedLink)
    {
        normalizedLink = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var candidate = value.Trim();
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) ||
            uri.IsFile ||
            uri.Scheme == Uri.UriSchemeHttp ||
            uri.Scheme == Uri.UriSchemeHttps)
        {
            return false;
        }

        normalizedLink = uri.AbsoluteUri;
        return true;
    }

    public static string? ExtractApplicationLinkFromHtml(string? htmlFormat)
    {
        if (string.IsNullOrWhiteSpace(htmlFormat))
        {
            return null;
        }

        foreach (Match match in HtmlHrefRegex().Matches(ExtractHtmlFragment(htmlFormat)))
        {
            if (TryNormalizeApplicationLink(
                    WebUtility.HtmlDecode(match.Groups["href"].Value),
                    out var applicationLink))
            {
                return applicationLink;
            }
        }

        return null;
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

    public static bool IsVideoFile(string? contentType, string? fileExtension) =>
        (!string.IsNullOrWhiteSpace(contentType) &&
         contentType.Trim().StartsWith("video/", StringComparison.OrdinalIgnoreCase)) ||
        (!string.IsNullOrWhiteSpace(fileExtension) &&
         VideoFileExtensions.Contains(fileExtension.Trim()));

    public static bool IsLikelyShellIconThumbnail(
        bool isReportedIcon,
        bool hasIntrinsicVisualContent,
        uint width,
        uint height)
    {
        if (isReportedIcon)
        {
            return true;
        }

        if (hasIntrinsicVisualContent || width == 0 || height == 0)
        {
            return false;
        }

        var longestEdge = Math.Max(width, height);
        var edgeDifference = Math.Abs((long)width - height);
        return edgeDifference * 20 <= longestEdge;
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

    public static bool IsColor(string? text) => TryNormalizeCssColor(text, out _);

    public static bool TryNormalizeCssColor(string? value, out string normalizedColor)
    {
        normalizedColor = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var candidate = value.Trim();
        if (TryNormalizeHexColor(candidate, out normalizedColor))
        {
            return true;
        }

        var match = CssColorFunctionRegex().Match(candidate);
        if (!match.Success)
        {
            return false;
        }

        var functionName = match.Groups["name"].Value;
        var arguments = match.Groups["arguments"].Value;
        return functionName.StartsWith("rgb", StringComparison.OrdinalIgnoreCase)
            ? TryNormalizeRgbColor(arguments, functionName.EndsWith('a'), out normalizedColor)
            : TryNormalizeHslColor(arguments, functionName.EndsWith('a'), out normalizedColor);
    }

    public static bool IsMarkdown(string? text, string? sourcePath = null) =>
        string.Equals(Path.GetExtension(sourcePath), ".md", StringComparison.OrdinalIgnoreCase) ||
        (!string.IsNullOrWhiteSpace(text) && MarkdownStructureRegex().IsMatch(text));

    public static bool IsJson(string? text, string? sourcePath = null)
    {
        if (string.Equals(Path.GetExtension(sourcePath), ".json", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var candidate = text?.Trim();
        if (string.IsNullOrEmpty(candidate) ||
            (candidate[0] != '{' && candidate[0] != '['))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(candidate);
            return document.RootElement.ValueKind is JsonValueKind.Object or JsonValueKind.Array;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static bool IsXml(string? text, string? sourcePath = null)
    {
        if (string.Equals(Path.GetExtension(sourcePath), ".xml", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var candidate = text?.Trim();
        if (string.IsNullOrEmpty(candidate) || candidate[0] != '<')
        {
            return false;
        }

        try
        {
            using var textReader = new StringReader(candidate);
            using var xmlReader = XmlReader.Create(
                textReader,
                new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null
                });
            var hasElement = false;
            while (xmlReader.Read())
            {
                hasElement |= xmlReader.NodeType == XmlNodeType.Element;
            }

            return hasElement;
        }
        catch (XmlException)
        {
            return false;
        }
    }

    public static bool IsDateTime(string? text)
    {
        var candidate = text?.Trim();
        return !string.IsNullOrEmpty(candidate) &&
               IsoDateTimeRegex().IsMatch(candidate) &&
               DateTimeOffset.TryParse(
                   candidate,
                   CultureInfo.InvariantCulture,
                   DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal,
                   out _);
    }

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

    private static bool TryNormalizeHexColor(string value, out string normalizedColor)
    {
        normalizedColor = string.Empty;
        if (value.Length is not (4 or 5 or 7 or 9) || value[0] != '#')
        {
            return false;
        }

        var digits = value.AsSpan(1);
        if (!digits.ToString().All(Uri.IsHexDigit))
        {
            return false;
        }

        if (digits.Length is 3 or 4)
        {
            var expanded = new char[(digits.Length * 2) + 1];
            expanded[0] = '#';
            for (var index = 0; index < digits.Length; index++)
            {
                var digit = char.ToUpperInvariant(digits[index]);
                expanded[(index * 2) + 1] = digit;
                expanded[(index * 2) + 2] = digit;
            }

            normalizedColor = new string(expanded);
            return true;
        }

        normalizedColor = $"#{digits.ToString().ToUpperInvariant()}";
        return true;
    }

    private static bool TryNormalizeRgbColor(
        string arguments,
        bool alphaRequired,
        out string normalizedColor)
    {
        normalizedColor = string.Empty;
        if (!TrySplitCssColorArguments(arguments, alphaRequired, out var components, out var alpha) ||
            !TryParseRgbComponent(components[0], out var red) ||
            !TryParseRgbComponent(components[1], out var green) ||
            !TryParseRgbComponent(components[2], out var blue) ||
            !TryParseOptionalAlpha(alpha, out var alphaByte))
        {
            return false;
        }

        normalizedColor = alpha is null
            ? $"#{red:X2}{green:X2}{blue:X2}"
            : $"#{red:X2}{green:X2}{blue:X2}{alphaByte:X2}";
        return true;
    }

    private static bool TryNormalizeHslColor(
        string arguments,
        bool alphaRequired,
        out string normalizedColor)
    {
        normalizedColor = string.Empty;
        if (!TrySplitCssColorArguments(arguments, alphaRequired, out var components, out var alpha) ||
            !TryParseHue(components[0], out var hue) ||
            !TryParsePercentage(components[1], out var saturation) ||
            !TryParsePercentage(components[2], out var lightness) ||
            !TryParseOptionalAlpha(alpha, out var alphaByte))
        {
            return false;
        }

        var chroma = (1d - Math.Abs((2d * lightness) - 1d)) * saturation;
        var hueSection = hue / 60d;
        var secondary = chroma * (1d - Math.Abs((hueSection % 2d) - 1d));
        var (red, green, blue) = ((int)Math.Floor(hueSection)) switch
        {
            0 => (chroma, secondary, 0d),
            1 => (secondary, chroma, 0d),
            2 => (0d, chroma, secondary),
            3 => (0d, secondary, chroma),
            4 => (secondary, 0d, chroma),
            _ => (chroma, 0d, secondary)
        };
        var offset = lightness - (chroma / 2d);
        var redByte = ToColorByte(red + offset);
        var greenByte = ToColorByte(green + offset);
        var blueByte = ToColorByte(blue + offset);
        normalizedColor = alpha is null
            ? $"#{redByte:X2}{greenByte:X2}{blueByte:X2}"
            : $"#{redByte:X2}{greenByte:X2}{blueByte:X2}{alphaByte:X2}";
        return true;
    }

    private static bool TrySplitCssColorArguments(
        string arguments,
        bool alphaRequired,
        out string[] components,
        out string? alpha)
    {
        alpha = null;
        if (arguments.Contains(','))
        {
            var commaSeparated = arguments.Split(
                ',',
                StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            var expectedCount = alphaRequired ? 4 : 3;
            if (commaSeparated.Length != expectedCount)
            {
                components = [];
                return false;
            }

            components = commaSeparated[..3];
            alpha = commaSeparated.Length == 4 ? commaSeparated[3] : null;
            return true;
        }

        var slashSeparated = arguments.Split('/', StringSplitOptions.TrimEntries);
        if (slashSeparated.Length > 2 || slashSeparated.Any(static part => part.Length == 0))
        {
            components = [];
            return false;
        }

        var whitespaceSeparated = slashSeparated[0].Split(
            default(char[]),
            StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (slashSeparated.Length == 2)
        {
            alpha = slashSeparated[1];
        }
        else if (alphaRequired && whitespaceSeparated.Length == 4)
        {
            alpha = whitespaceSeparated[3];
            whitespaceSeparated = whitespaceSeparated[..3];
        }

        if (whitespaceSeparated.Length != 3 || alphaRequired && alpha is null)
        {
            components = [];
            return false;
        }

        components = whitespaceSeparated;
        return true;
    }

    private static bool TryParseRgbComponent(string value, out byte component)
    {
        var isPercentage = value.EndsWith('%');
        var numericValue = isPercentage ? value[..^1] : value;
        var maximum = isPercentage ? 100d : 255d;
        if (!double.TryParse(
                numericValue,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var parsed) ||
            !double.IsFinite(parsed) ||
            parsed < 0d ||
            parsed > maximum)
        {
            component = 0;
            return false;
        }

        component = ToColorByte(parsed / maximum);
        return true;
    }

    private static bool TryParseOptionalAlpha(string? value, out byte alpha)
    {
        if (value is null)
        {
            alpha = byte.MaxValue;
            return true;
        }

        var isPercentage = value.EndsWith('%');
        var numericValue = isPercentage ? value[..^1] : value;
        var maximum = isPercentage ? 100d : 1d;
        if (!double.TryParse(
                numericValue,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var parsed) ||
            !double.IsFinite(parsed) ||
            parsed < 0d ||
            parsed > maximum)
        {
            alpha = 0;
            return false;
        }

        alpha = ToColorByte(parsed / maximum);
        return true;
    }

    private static bool TryParseHue(string value, out double hue)
    {
        var multiplier = 1d;
        var numericValue = value;
        if (value.EndsWith("turn", StringComparison.OrdinalIgnoreCase))
        {
            multiplier = 360d;
            numericValue = value[..^4];
        }
        else if (value.EndsWith("grad", StringComparison.OrdinalIgnoreCase))
        {
            multiplier = 0.9d;
            numericValue = value[..^4];
        }
        else if (value.EndsWith("rad", StringComparison.OrdinalIgnoreCase))
        {
            multiplier = 180d / Math.PI;
            numericValue = value[..^3];
        }
        else if (value.EndsWith("deg", StringComparison.OrdinalIgnoreCase))
        {
            numericValue = value[..^3];
        }

        if (!double.TryParse(
                numericValue,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var parsed) ||
            !double.IsFinite(parsed))
        {
            hue = 0d;
            return false;
        }

        hue = ((parsed * multiplier) % 360d + 360d) % 360d;
        return true;
    }

    private static bool TryParsePercentage(string value, out double percentage)
    {
        if (!value.EndsWith('%') ||
            !double.TryParse(
                value[..^1],
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var parsed) ||
            !double.IsFinite(parsed) ||
            parsed < 0d ||
            parsed > 100d)
        {
            percentage = 0d;
            return false;
        }

        percentage = parsed / 100d;
        return true;
    }

    private static byte ToColorByte(double value) =>
        (byte)Math.Clamp(
            (int)Math.Round(value * byte.MaxValue, MidpointRounding.AwayFromZero),
            byte.MinValue,
            byte.MaxValue);

    [GeneratedRegex(@"(?im)^SourceURL:\s*(\S+)\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex SourceUrlHeaderRegex();

    [GeneratedRegex(@"(?is)<(?:script|style)\b[^>]*>.*?</(?:script|style)\s*>", RegexOptions.CultureInvariant)]
    private static partial Regex ScriptAndStyleRegex();

    [GeneratedRegex(@"(?s)<[^>]+>", RegexOptions.CultureInvariant)]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex(@"(?is)\bhref\s*=\s*(?:""(?<href>[^""]*)""|'(?<href>[^']*)'|(?<href>[^\s>]+))", RegexOptions.CultureInvariant)]
    private static partial Regex HtmlHrefRegex();

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

    [GeneratedRegex(@"^(?<name>rgba?|hsla?)\s*\((?<arguments>[^\r\n()]*)\)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CssColorFunctionRegex();

    [GeneratedRegex(@"(?m)^(?:\s{0,3}#{1,6}\s+|\s*[-*+]\s+|\s*\d+[.)]\s+|>\s+)|\[[^\]\r\n]+\]\([^\)\r\n]+\)", RegexOptions.CultureInvariant)]
    private static partial Regex MarkdownStructureRegex();

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}(?:[T\s]\d{2}:\d{2}(?::\d{2}(?:\.\d+)?)?(?:\s?(?:Z|[+-]\d{2}:?\d{2}))?)?$", RegexOptions.CultureInvariant)]
    private static partial Regex IsoDateTimeRegex();
}
