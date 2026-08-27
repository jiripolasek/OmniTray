// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

namespace OmniTray.Core;

[Flags]
public enum ContentRepresentations
{
    None = 0,
    Text = 1 << 0,
    Html = 1 << 1,
    Rtf = 1 << 2,
    Bitmap = 1 << 3,
    StorageItem = 1 << 4,
    WebLink = 1 << 5,
    ApplicationLink = 1 << 6,
    Custom = 1 << 7
}

[Flags]
public enum ContentActions
{
    None = 0,
    Open = 1 << 0,
    Reveal = 1 << 1,
    Copy = 1 << 2,
    Cut = 1 << 3,
    Delete = 1 << 4,
    Share = 1 << 5,
    ShowProperties = 1 << 6,
    OpenSource = 1 << 7
}

[Flags]
public enum ContentFacets
{
    None = 0,
    Tabular = 1 << 0,
    Code = 1 << 1,
    Email = 1 << 2,
    Color = 1 << 3,
    Markdown = 1 << 4,
    Json = 1 << 5,
    DateTime = 1 << 6,
    OcrText = 1 << 7
}

public sealed class ContentProperty : IEquatable<ContentProperty>
{
    private readonly Func<ContentMetadata, bool> _matches;

    public ContentProperty(
        string id,
        string requirementDescription,
        Func<ContentMetadata, bool> matches)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("A content property ID is required.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(requirementDescription))
        {
            throw new ArgumentException("A content property description is required.", nameof(requirementDescription));
        }

        ArgumentNullException.ThrowIfNull(matches);
        this.Id = id.Trim();
        this.RequirementDescription = requirementDescription.Trim();
        this._matches = matches;
    }

    public string Id { get; }

    public string RequirementDescription { get; }

    public static ContentProperty HasLocalPath { get; } =
        new("omnitray.has-local-path", "have an available local path", static metadata => metadata.HasLocalPath);

    public static ContentProperty HasOriginalPath { get; } =
        new("omnitray.has-original-path", "have its original path", static metadata => metadata.HasOriginalPath);

    public static ContentProperty HasText { get; } =
        HasRepresentation("omnitray.has-text", "have text", ContentRepresentations.Text);

    public static ContentProperty HasHtml { get; } =
        HasRepresentation("omnitray.has-html", "have HTML", ContentRepresentations.Html);

    public static ContentProperty HasRtf { get; } =
        HasRepresentation("omnitray.has-rtf", "have rich text", ContentRepresentations.Rtf);

    public static ContentProperty HasBitmap { get; } =
        HasRepresentation("omnitray.has-bitmap", "have a bitmap", ContentRepresentations.Bitmap);

    public static ContentProperty HasImageFile { get; } =
        new("omnitray.has-image-file", "have an image file", static metadata => metadata.HasImageFile);

    public static ContentProperty HasStorageItem { get; } =
        HasRepresentation(
            "omnitray.has-storage-item",
            "have a file or folder representation",
            ContentRepresentations.StorageItem);

    public static ContentProperty HasWebLink { get; } =
        HasRepresentation("omnitray.has-web-link", "have a web link", ContentRepresentations.WebLink);

    public static ContentProperty HasApplicationLink { get; } =
        HasRepresentation(
            "omnitray.has-application-link",
            "have an application link",
            ContentRepresentations.ApplicationLink);

    public static ContentProperty HasCustomFormat { get; } =
        HasRepresentation("omnitray.has-custom-format", "have a native data format", ContentRepresentations.Custom);

    public static ContentProperty HasFile { get; } =
        new("omnitray.has-file", "be a file", static metadata => metadata.HasFile);

    public static ContentProperty HasFolder { get; } =
        new("omnitray.has-folder", "be a folder", static metadata => metadata.HasFolder);

    public static ContentProperty CanOpen { get; } =
        HasAction("omnitray.can-open", "be openable", ContentActions.Open);

    public static ContentProperty CanReveal { get; } =
        HasAction("omnitray.can-reveal", "be revealable", ContentActions.Reveal);

    public static ContentProperty CanCopy { get; } =
        HasAction("omnitray.can-copy", "be copyable", ContentActions.Copy);

    public static ContentProperty CanCut { get; } =
        HasAction("omnitray.can-cut", "be cuttable", ContentActions.Cut);

    public static ContentProperty CanDelete { get; } =
        HasAction("omnitray.can-delete", "be deletable", ContentActions.Delete);

    public static ContentProperty CanShare { get; } =
        HasAction("omnitray.can-share", "be shareable", ContentActions.Share);

    public static ContentProperty IsTabular { get; } =
        HasFacet("omnitray.is-tabular", "contain tabular content", ContentFacets.Tabular);

    public static ContentProperty IsCode { get; } =
        HasFacet("omnitray.is-code", "contain code", ContentFacets.Code);

    public static ContentProperty IsEmail { get; } =
        HasFacet("omnitray.is-email", "contain an email address", ContentFacets.Email);

    public static ContentProperty IsColor { get; } =
        HasFacet("omnitray.is-color", "contain a color", ContentFacets.Color);

    public static ContentProperty IsMarkdown { get; } =
        HasFacet("omnitray.is-markdown", "contain Markdown", ContentFacets.Markdown);

    public static ContentProperty IsJson { get; } =
        HasFacet("omnitray.is-json", "contain JSON", ContentFacets.Json);

    public static ContentProperty IsDateTime { get; } =
        HasFacet("omnitray.is-date-time", "contain a date or time", ContentFacets.DateTime);

    public static ContentProperty IsOcrText { get; } =
        HasFacet("omnitray.is-ocr-text", "contain OCR text", ContentFacets.OcrText);

    public bool IsSatisfiedBy(ContentMetadata metadata) => this._matches(metadata);

    public bool Equals(ContentProperty? other) =>
        other is not null && string.Equals(this.Id, other.Id, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is ContentProperty other && this.Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(this.Id);

    public override string ToString() => this.Id;

    private static ContentProperty HasRepresentation(
        string id,
        string description,
        ContentRepresentations representation) =>
        new(id, description, metadata => metadata.Representations.HasFlag(representation));

    private static ContentProperty HasAction(
        string id,
        string description,
        ContentActions action) =>
        new(id, description, metadata => metadata.Actions.HasFlag(action));

    private static ContentProperty HasFacet(
        string id,
        string description,
        ContentFacets facet) =>
        new(id, description, metadata => metadata.Facets.HasFlag(facet));
}

public readonly record struct ContentMetadata(
    ContentRepresentations Representations,
    ContentActions Actions,
    ContentFacets Facets,
    IReadOnlyList<ContentTag> Tags,
    bool HasLocalPath,
    bool HasOriginalPath,
    bool HasImageFile,
    bool HasFile,
    bool HasFolder);

public static class ContentMetadataPolicy
{
    private const ContentRepresentations ShareableRepresentations =
        ContentRepresentations.Text |
        ContentRepresentations.Html |
        ContentRepresentations.Rtf |
        ContentRepresentations.Bitmap |
        ContentRepresentations.StorageItem |
        ContentRepresentations.WebLink |
        ContentRepresentations.ApplicationLink;

    public static ContentClassifierRegistry Classifiers { get; } =
        ContentClassifierRegistry.Default;

    public static ContentMetadataProviderRegistry Providers { get; } =
        ContentMetadataProviderRegistry.Default;

    public static ContentMetadata GetMetadata(DropItem item) =>
        GetMetadata(item, Providers, Classifiers);

    public static ContentMetadata GetMetadata(
        DropItem item,
        ContentClassifierRegistry classifiers) =>
        GetMetadata(item, Providers, classifiers);

    public static ContentMetadata GetMetadata(
        DropItem item,
        ContentMetadataProviderRegistry providers,
        ContentClassifierRegistry classifiers)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(classifiers);

        var contribution = providers.Compose(item).Contribution;
        var classification = classifiers.Classify(item);

        return new ContentMetadata(
            contribution.Representations,
            contribution.Actions,
            classification.Facets,
            classification.Tags,
            contribution.HasLocalPath,
            contribution.HasOriginalPath,
            contribution.HasImageFile,
            contribution.HasFile,
            contribution.HasFolder);
    }

    public static ContentMetadata CreatePotential(ContentRepresentations representations)
    {
        var hasStorageItem = Has(representations, ContentRepresentations.StorageItem);
        var actions = ContentActions.None;
        if (representations != ContentRepresentations.None)
        {
            actions |= ContentActions.Copy;
        }

        if ((representations & ShareableRepresentations) != 0)
        {
            actions |= ContentActions.Share;
        }

        if (hasStorageItem ||
            Has(representations, ContentRepresentations.WebLink) ||
            Has(representations, ContentRepresentations.ApplicationLink))
        {
            actions |= ContentActions.Open;
        }

        // Drag-over exposes formats, not resolved paths or ownership. Stay permissive here;
        // the executor validates the materialized items again after the drop.
        if (hasStorageItem)
        {
            actions |= ContentActions.Reveal |
                       ContentActions.Cut |
                       ContentActions.Delete |
                       ContentActions.ShowProperties;
        }

        return new ContentMetadata(
            representations,
            actions,
            ContentFacets.None,
            [],
            hasStorageItem,
            hasStorageItem,
            hasStorageItem || Has(representations, ContentRepresentations.Bitmap),
            hasStorageItem,
            hasStorageItem);
    }

    public static bool HasAction(DropItem item, ContentActions action) =>
        Has(GetMetadata(item).Actions, action);

    public static bool Matches(ContentMetadata metadata, ContentProperty property)
    {
        ArgumentNullException.ThrowIfNull(property);
        return property.IsSatisfiedBy(metadata);
    }

    private static bool Has(ContentRepresentations representations, ContentRepresentations requested) =>
        (representations & requested) == requested;

    private static bool Has(ContentActions actions, ContentActions requested) =>
        (actions & requested) == requested;

}
