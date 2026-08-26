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
    Color = 1 << 3
}

public enum ContentProperty
{
    HasLocalPath,
    HasOriginalPath,
    HasText,
    HasHtml,
    HasRtf,
    HasBitmap,
    HasImageFile,
    HasStorageItem,
    HasWebLink,
    HasApplicationLink,
    HasCustomFormat,
    HasFile,
    HasFolder,
    CanOpen,
    CanReveal,
    CanCopy,
    CanCut,
    CanDelete,
    CanShare,
    IsTabular,
    IsCode,
    IsEmail,
    IsColor
}

public readonly record struct ContentMetadata(
    ContentRepresentations Representations,
    ContentActions Actions,
    ContentFacets Facets,
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

    public static ContentMetadata GetMetadata(DropItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        var hasLocalPath = !string.IsNullOrWhiteSpace(item.SourcePath) &&
                           item.Kind is DropItemKind.File or DropItemKind.Folder or DropItemKind.Image;
        var hasOriginalPath = hasLocalPath && !item.IsOwned;
        var isTableShapedCapture = ContentDetection.ContainsHtmlTable(item.Html);
        var hasImageFile = item.Kind == DropItemKind.Image && hasLocalPath && !isTableShapedCapture;
        var representations = GetRepresentations(item, hasLocalPath, hasImageFile, isTableShapedCapture);
        var actions = GetActions(item, representations, hasLocalPath, hasOriginalPath);

        return new ContentMetadata(
            representations,
            actions,
            GetFacets(item),
            hasLocalPath,
            hasOriginalPath,
            hasImageFile,
            item.Kind == DropItemKind.File,
            item.Kind == DropItemKind.Folder);
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
            hasStorageItem,
            hasStorageItem,
            hasStorageItem || Has(representations, ContentRepresentations.Bitmap),
            hasStorageItem,
            hasStorageItem);
    }

    public static bool HasAction(DropItem item, ContentActions action) =>
        Has(GetMetadata(item).Actions, action);

    public static bool Matches(ContentMetadata metadata, ContentProperty property) => property switch
    {
        ContentProperty.HasLocalPath => metadata.HasLocalPath,
        ContentProperty.HasOriginalPath => metadata.HasOriginalPath,
        ContentProperty.HasText => Has(metadata.Representations, ContentRepresentations.Text),
        ContentProperty.HasHtml => Has(metadata.Representations, ContentRepresentations.Html),
        ContentProperty.HasRtf => Has(metadata.Representations, ContentRepresentations.Rtf),
        ContentProperty.HasBitmap => Has(metadata.Representations, ContentRepresentations.Bitmap),
        ContentProperty.HasImageFile => metadata.HasImageFile,
        ContentProperty.HasStorageItem => Has(metadata.Representations, ContentRepresentations.StorageItem),
        ContentProperty.HasWebLink => Has(metadata.Representations, ContentRepresentations.WebLink),
        ContentProperty.HasApplicationLink => Has(metadata.Representations, ContentRepresentations.ApplicationLink),
        ContentProperty.HasCustomFormat => Has(metadata.Representations, ContentRepresentations.Custom),
        ContentProperty.HasFile => metadata.HasFile,
        ContentProperty.HasFolder => metadata.HasFolder,
        ContentProperty.CanOpen => Has(metadata.Actions, ContentActions.Open),
        ContentProperty.CanReveal => Has(metadata.Actions, ContentActions.Reveal),
        ContentProperty.CanCopy => Has(metadata.Actions, ContentActions.Copy),
        ContentProperty.CanCut => Has(metadata.Actions, ContentActions.Cut),
        ContentProperty.CanDelete => Has(metadata.Actions, ContentActions.Delete),
        ContentProperty.CanShare => Has(metadata.Actions, ContentActions.Share),
        ContentProperty.IsTabular => Has(metadata.Facets, ContentFacets.Tabular),
        ContentProperty.IsCode => Has(metadata.Facets, ContentFacets.Code),
        ContentProperty.IsEmail => Has(metadata.Facets, ContentFacets.Email),
        ContentProperty.IsColor => Has(metadata.Facets, ContentFacets.Color),
        _ => false
    };

    private static ContentRepresentations GetRepresentations(
        DropItem item,
        bool hasLocalPath,
        bool hasImageFile,
        bool isTableShapedCapture)
    {
        var representations = ContentRepresentations.None;
        if (!string.IsNullOrWhiteSpace(item.Text))
        {
            representations |= ContentRepresentations.Text;
        }

        if (!string.IsNullOrWhiteSpace(item.Html))
        {
            representations |= ContentRepresentations.Html;
        }

        if (!string.IsNullOrWhiteSpace(item.Rtf))
        {
            representations |= ContentRepresentations.Rtf;
        }

        if (hasImageFile)
        {
            representations |= ContentRepresentations.Bitmap;
        }

        if (hasLocalPath &&
            (item.Kind is DropItemKind.File or DropItemKind.Folder ||
             (item.Kind == DropItemKind.Image && !isTableShapedCapture)))
        {
            representations |= ContentRepresentations.StorageItem;
        }

        if (item.Kind == DropItemKind.Uri && ContentDetection.TryNormalizeWebUrl(item.Url, out _))
        {
            representations |= ContentRepresentations.WebLink;
        }

        if (TryNormalizeAbsoluteUri(item.ApplicationLink, out _))
        {
            representations |= ContentRepresentations.ApplicationLink;
        }

        if (item.CustomFormats.Count > 0)
        {
            representations |= ContentRepresentations.Custom;
        }

        return representations;
    }

    private static ContentActions GetActions(
        DropItem item,
        ContentRepresentations representations,
        bool hasLocalPath,
        bool hasOriginalPath)
    {
        var actions = representations == ContentRepresentations.None
            ? ContentActions.None
            : ContentActions.Copy;
        if ((representations & ShareableRepresentations) != 0)
        {
            actions |= ContentActions.Share;
        }

        if (hasLocalPath)
        {
            actions |= ContentActions.Open |
                       ContentActions.Reveal |
                       ContentActions.ShowProperties;
        }

        if (Has(representations, ContentRepresentations.WebLink) ||
            Has(representations, ContentRepresentations.ApplicationLink))
        {
            actions |= ContentActions.Open;
        }

        if (hasOriginalPath && Has(representations, ContentRepresentations.StorageItem))
        {
            actions |= ContentActions.Cut | ContentActions.Delete;
        }

        if (ContentDetection.TryNormalizeWebUrl(item.SourceUrl, out _))
        {
            actions |= ContentActions.OpenSource;
        }

        return actions;
    }

    private static ContentFacets GetFacets(DropItem item)
    {
        var facets = ContentFacets.None;
        if (ContentDetection.IsTabular(item.Text, item.Html, item.Rtf))
        {
            facets |= ContentFacets.Tabular;
        }

        if (ContentDetection.IsCode(item.Text, item.Html))
        {
            facets |= ContentFacets.Code;
        }

        if (ContentDetection.IsEmail(item.Text, item.ApplicationLink))
        {
            facets |= ContentFacets.Email;
        }

        if (ContentDetection.IsColor(item.Text))
        {
            facets |= ContentFacets.Color;
        }

        return facets;
    }

    private static bool TryNormalizeAbsoluteUri(string? value, out Uri? uri)
    {
        uri = null;
        if (string.IsNullOrWhiteSpace(value) ||
            !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var candidate))
        {
            return false;
        }

        uri = candidate;
        return true;
    }

    private static bool Has(ContentRepresentations representations, ContentRepresentations requested) =>
        (representations & requested) == requested;

    private static bool Has(ContentActions actions, ContentActions requested) =>
        (actions & requested) == requested;

    private static bool Has(ContentFacets facets, ContentFacets requested) =>
        (facets & requested) == requested;
}
