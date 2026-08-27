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
        string? applicationLink = null,
        string? sourcePackageFamilyName = null,
        string? sourceApplicationLink = null,
        DropCaptureMetadata? capture = null,
        ContentBacking? backing = null,
        DropFileFacts? fileFacts = null,
        IReadOnlyList<DropItemHtmlResource>? htmlResources = null)
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
        this.Provenance = new ContentProvenance
        {
            ApplicationName = NormalizeOptionalValue(sourceApplicationName),
            PackageFamilyName = NormalizeOptionalValue(sourcePackageFamilyName),
            SourceWebLink = NormalizeOptionalUrl(sourceUrl),
            SourceApplicationLink = NormalizeOptionalAbsoluteUri(sourceApplicationLink)
        };
        this.Capture = NormalizeCapture(capture);
        this.Backing = NormalizeBacking(backing, kind, sourcePath, isOwned);
        this.FileFacts = NormalizeFileFacts(fileFacts);
        this.HtmlResources = NormalizeHtmlResources(htmlResources);
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

    public string? SourcePackageFamilyName => this.Provenance.PackageFamilyName;

    public string? SourceApplicationLink => this.Provenance.SourceApplicationLink;

    public ContentProvenance Provenance { get; }

    public DropCaptureMetadata? Capture { get; }

    public ContentBacking Backing { get; }

    public DropFileFacts? FileFacts { get; }

    public IReadOnlyList<DropItemHtmlResource> HtmlResources { get; }

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
            NormalizeOptionalAbsoluteUri(applicationLink) ?? this.ApplicationLink,
            this.SourcePackageFamilyName,
            this.SourceApplicationLink,
            this.Capture,
            this.Backing,
            this.FileFacts,
            this.HtmlResources);
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
            this.ApplicationLink,
            this.SourcePackageFamilyName,
            this.SourceApplicationLink,
            this.Capture,
            this.Backing,
            this.FileFacts,
            this.HtmlResources);
    }

    public DropItem WithMetadata(
        ContentProvenance? provenance = null,
        DropCaptureMetadata? capture = null,
        ContentBacking? backing = null,
        DropFileFacts? fileFacts = null,
        IReadOnlyList<DropItemHtmlResource>? htmlResources = null)
    {
        provenance ??= this.Provenance;
        return new DropItem(
            this.Id,
            this.Kind,
            this.DisplayName,
            this.SourcePath,
            this.Text,
            this.Html,
            this.Rtf,
            this.Url,
            provenance.SourceWebLink,
            provenance.ApplicationName,
            this.CustomFormats,
            this.IsOwned,
            this.CreatedAt,
            this.ApplicationLink,
            provenance.PackageFamilyName,
            provenance.SourceApplicationLink,
            capture ?? this.Capture,
            backing ?? this.Backing,
            fileFacts ?? this.FileFacts,
            htmlResources ?? this.HtmlResources);
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
        string? applicationLink = null,
        string? sourcePackageFamilyName = null,
        string? sourceApplicationLink = null,
        DropCaptureMetadata? capture = null,
        ContentBacking? backing = null,
        DropFileFacts? fileFacts = null,
        IReadOnlyList<DropItemHtmlResource>? htmlResources = null)
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
            applicationLink,
            sourcePackageFamilyName,
            sourceApplicationLink,
            capture,
            backing,
            fileFacts,
            htmlResources);
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

    private static DropCaptureMetadata? NormalizeCapture(DropCaptureMetadata? capture)
    {
        if (capture is null || capture.CaptureId == Guid.Empty || capture.Ordinal < 0)
        {
            return null;
        }

        return capture with
        {
            Formats = capture.Formats
                .Where(static format => !string.IsNullOrWhiteSpace(format.FormatId))
                .Select(static format => format with
                {
                    FormatId = format.FormatId.Trim(),
                    Detail = NormalizeOptionalValue(format.Detail)
                })
                .DistinctBy(static format => format.FormatId, StringComparer.Ordinal)
                .ToArray()
        };
    }

    private static ContentBacking NormalizeBacking(
        ContentBacking? backing,
        DropItemKind kind,
        string? sourcePath,
        bool isOwned)
    {
        var path = NormalizeOptionalValue(backing?.Path) ?? NormalizeOptionalValue(sourcePath);
        var inferredKind = string.IsNullOrWhiteSpace(path)
            ? ContentBackingKind.None
            : !isOwned
                ? ContentBackingKind.OriginalPath
                : kind is DropItemKind.Text or DropItemKind.Image
                    ? ContentBackingKind.GeneratedProjection
                    : ContentBackingKind.ManagedSnapshot;
        return new ContentBacking
        {
            Kind = backing?.Kind ?? inferredKind,
            Path = path
        };
    }

    private static DropFileFacts? NormalizeFileFacts(DropFileFacts? facts) =>
        facts is null || string.IsNullOrWhiteSpace(facts.OriginalFileName)
            ? null
            : facts with
            {
                OriginalFileName = facts.OriginalFileName.Trim(),
                ContentType = NormalizeOptionalValue(facts.ContentType),
                Sha256 = NormalizeOptionalValue(facts.Sha256)
            };

    private static IReadOnlyList<DropItemHtmlResource> NormalizeHtmlResources(
        IReadOnlyList<DropItemHtmlResource>? resources) => resources is null
        ? []
        : resources
            .Where(static resource =>
                !string.IsNullOrWhiteSpace(resource.ResourceKey) &&
                !string.IsNullOrWhiteSpace(resource.ManagedRelativePath))
            .Select(static resource => resource with
            {
                ResourceKey = resource.ResourceKey.Trim(),
                ManagedRelativePath = resource.ManagedRelativePath.Trim()
            })
            .DistinctBy(static resource => resource.ResourceKey, StringComparer.Ordinal)
            .ToArray();

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
