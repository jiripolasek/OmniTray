// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

namespace OmniTray.Core;

public enum DropItemKind
{
    File,
    Folder,
    Text,
    Image,
    Uri
}

public sealed record DropItem
{
    private DropItem(
        Guid id,
        DropItemKind kind,
        string displayName,
        string? sourcePath,
        string? text,
        string? html,
        string? rtf,
        string? url,
        string? sourceUrl,
        string? sourceApplicationName,
        IReadOnlyList<DropItemDataFormat>? customFormats,
        bool isOwned,
        DateTimeOffset createdAt,
        string? applicationLink = null)
    {
        this.Id = id;
        this.Kind = kind;
        this.DisplayName = displayName;
        this.SourcePath = sourcePath;
        this.Text = text;
        this.Html = html;
        this.Rtf = rtf;
        this.Url = url;
        this.SourceUrl = sourceUrl;
        this.SourceApplicationName = sourceApplicationName;
        this.CustomFormats = NormalizeCustomFormats(customFormats);
        this.IsOwned = isOwned;
        this.CreatedAt = createdAt;
        this.ApplicationLink = NormalizeOptionalAbsoluteUri(applicationLink);
    }

    public Guid Id { get; }

    public DropItemKind Kind { get; }

    public string DisplayName { get; }

    public string? SourcePath { get; }

    public string? Text { get; }

    public string? Html { get; }

    public string? Rtf { get; }

    public string? Url { get; }

    public string? SourceUrl { get; }

    public string? SourceApplicationName { get; }

    public string? ApplicationLink { get; }

    public IReadOnlyList<DropItemDataFormat> CustomFormats { get; }

    public bool IsOwned { get; }

    public DateTimeOffset CreatedAt { get; }

    public static DropItem CreateStorageItem(
        string displayName,
        string? sourcePath,
        bool isFolder,
        bool isOwned = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        return new DropItem(
            Guid.NewGuid(),
            isFolder ? DropItemKind.Folder : DropItemKind.File,
            displayName.Trim(),
            sourcePath,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            isOwned,
            DateTimeOffset.UtcNow);
    }

    public static DropItem CreateText(
        string text,
        string? sourcePath = null,
        bool isOwned = false,
        string? html = null,
        string? rtf = null,
        string? sourceUrl = null,
        string? sourceApplicationName = null,
        string? applicationLink = null)
    {
        return CreateRichText(
            text,
            html,
            rtf,
            sourcePath,
            isOwned,
            sourceUrl,
            sourceApplicationName,
            applicationLink);
    }

    public static DropItem CreateRichText(
        string? text,
        string? html,
        string? rtf,
        string? sourcePath = null,
        bool isOwned = false,
        string? sourceUrl = null,
        string? sourceApplicationName = null,
        string? applicationLink = null)
    {
        if (string.IsNullOrWhiteSpace(text) &&
            string.IsNullOrWhiteSpace(html) &&
            string.IsNullOrWhiteSpace(rtf))
        {
            throw new ArgumentException("At least one text representation is required.", nameof(text));
        }

        if (isOwned)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        }

        var preview = !string.IsNullOrWhiteSpace(text)
            ? text
            : !string.IsNullOrWhiteSpace(html)
                ? ContentDetection.ExtractPlainTextFromHtml(html)
                : null;
        var displayName = CreateCompactDisplayName(preview, "Rich text");

        return new DropItem(
            Guid.NewGuid(),
            DropItemKind.Text,
            displayName,
            sourcePath,
            text,
            html,
            rtf,
            null,
            NormalizeOptionalUrl(sourceUrl),
            NormalizeOptionalValue(sourceApplicationName),
            null,
            isOwned,
            DateTimeOffset.UtcNow,
            applicationLink);
    }

    public static DropItem CreateImage(
        string displayName,
        string sourcePath,
        bool isOwned = false,
        string? text = null,
        string? html = null,
        string? rtf = null,
        string? sourceUrl = null,
        string? sourceApplicationName = null,
        string? applicationLink = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        return new DropItem(
            Guid.NewGuid(),
            DropItemKind.Image,
            displayName.Trim(),
            sourcePath,
            text,
            html,
            rtf,
            null,
            NormalizeOptionalUrl(sourceUrl),
            NormalizeOptionalValue(sourceApplicationName),
            null,
            isOwned,
            DateTimeOffset.UtcNow,
            applicationLink);
    }

    public static DropItem CreateUri(
        string url,
        string? displayName = null,
        string? text = null,
        string? html = null,
        string? rtf = null,
        string? sourceUrl = null,
        string? sourceApplicationName = null,
        string? applicationLink = null)
    {
        if (!ContentDetection.TryNormalizeWebUrl(url, out var normalizedUrl))
        {
            throw new ArgumentException("A valid HTTP or HTTPS URL is required.", nameof(url));
        }

        var resolvedDisplayName = string.IsNullOrWhiteSpace(displayName) ||
                                  string.Equals(displayName.Trim(), normalizedUrl, StringComparison.OrdinalIgnoreCase)
            ? ContentDetection.CreateUrlDisplayName(normalizedUrl)
            : CreateCompactDisplayName(displayName, ContentDetection.CreateUrlDisplayName(normalizedUrl));

        return new DropItem(
            Guid.NewGuid(),
            DropItemKind.Uri,
            resolvedDisplayName,
            null,
            string.IsNullOrWhiteSpace(text) ? normalizedUrl : text,
            html,
            rtf,
            normalizedUrl,
            NormalizeOptionalUrl(sourceUrl) ?? normalizedUrl,
            NormalizeOptionalValue(sourceApplicationName),
            null,
            false,
            DateTimeOffset.UtcNow,
            applicationLink);
    }

    public DropItem WithRepresentations(
        string? text = null,
        string? html = null,
        string? rtf = null,
        string? sourceUrl = null,
        string? sourceApplicationName = null,
        string? applicationLink = null)
    {
        return new DropItem(
            this.Id,
            this.Kind,
            this.DisplayName,
            this.SourcePath,
            string.IsNullOrWhiteSpace(text) ? this.Text : text,
            string.IsNullOrWhiteSpace(html) ? this.Html : html,
            string.IsNullOrWhiteSpace(rtf) ? this.Rtf : rtf,
            this.Url,
            NormalizeOptionalUrl(sourceUrl) ?? this.SourceUrl,
            NormalizeOptionalValue(sourceApplicationName) ?? this.SourceApplicationName,
            this.CustomFormats,
            this.IsOwned,
            this.CreatedAt,
            NormalizeOptionalAbsoluteUri(applicationLink) ?? this.ApplicationLink);
    }

    public DropItem WithCustomFormats(IReadOnlyList<DropItemDataFormat>? customFormats)
    {
        return new DropItem(
            this.Id,
            this.Kind,
            this.DisplayName,
            this.SourcePath,
            this.Text,
            this.Html,
            this.Rtf,
            this.Url,
            this.SourceUrl,
            this.SourceApplicationName,
            customFormats,
            this.IsOwned,
            this.CreatedAt,
            this.ApplicationLink);
    }

    public static DropItem Restore(
        Guid id,
        DropItemKind kind,
        string displayName,
        string? sourcePath,
        string? text,
        string? html,
        string? rtf,
        string? url,
        string? sourceUrl,
        string? sourceApplicationName,
        bool isOwned,
        DateTimeOffset createdAt,
        IReadOnlyList<DropItemDataFormat>? customFormats = null,
        string? applicationLink = null)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("An item ID is required.", nameof(id));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        if (kind == DropItemKind.Text &&
            string.IsNullOrWhiteSpace(text) &&
            string.IsNullOrWhiteSpace(html) &&
            string.IsNullOrWhiteSpace(rtf))
        {
            throw new ArgumentException("A text item requires at least one text representation.", nameof(text));
        }

        if (kind == DropItemKind.Image)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        }

        if (kind == DropItemKind.Uri && !ContentDetection.TryNormalizeWebUrl(url, out url))
        {
            throw new ArgumentException("A URL item requires a valid HTTP or HTTPS URL.", nameof(url));
        }

        return new DropItem(
            id,
            kind,
            displayName.Trim(),
            sourcePath,
            text,
            html,
            rtf,
            url,
            NormalizeOptionalUrl(sourceUrl),
            NormalizeOptionalValue(sourceApplicationName),
            customFormats,
            isOwned,
            createdAt,
            applicationLink);
    }

    public static DropItem Restore(
        Guid id,
        DropItemKind kind,
        string displayName,
        string? sourcePath,
        string? text,
        bool isOwned,
        DateTimeOffset createdAt) => Restore(
        id,
        kind,
        displayName,
        sourcePath,
        text,
        null,
        null,
        null,
        null,
        null,
        isOwned,
        createdAt);

    private static IReadOnlyList<DropItemDataFormat> NormalizeCustomFormats(
        IReadOnlyList<DropItemDataFormat>? customFormats)
    {
        if (customFormats is null || customFormats.Count == 0)
        {
            return [];
        }

        var formats = new List<DropItemDataFormat>(customFormats.Count);
        var formatIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var format in customFormats)
        {
            ArgumentNullException.ThrowIfNull(format);
            if (formatIds.Add(format.FormatId))
            {
                formats.Add(format);
            }
        }

        return formats;
    }

    private static string CreateCompactDisplayName(string? value, string fallback)
    {
        var normalized = string.Join(
            ' ',
            (value ?? string.Empty).Split(default(char[]), StringSplitOptions.RemoveEmptyEntries));
        if (string.IsNullOrEmpty(normalized))
        {
            return fallback;
        }

        return normalized.Length <= 48 ? normalized : $"{normalized[..47]}…";
    }

    private static string? NormalizeOptionalUrl(string? value) =>
        ContentDetection.TryNormalizeWebUrl(value, out var normalized) ? normalized : null;

    private static string? NormalizeOptionalValue(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeOptionalAbsoluteUri(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
            ? uri.AbsoluteUri
            : null;
}
