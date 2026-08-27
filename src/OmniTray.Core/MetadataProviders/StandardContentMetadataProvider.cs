// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

namespace OmniTray.Core.MetadataProviders;

internal sealed class StandardContentMetadataProvider : IContentMetadataProvider
{
    private const ContentRepresentations ShareableRepresentations =
        ContentRepresentations.Text |
        ContentRepresentations.Html |
        ContentRepresentations.Rtf |
        ContentRepresentations.Bitmap |
        ContentRepresentations.StorageItem |
        ContentRepresentations.WebLink |
        ContentRepresentations.ApplicationLink;

    public string Id => "omnitray.builtin.standard-metadata";

    public string DisplayName => "Standard content metadata";

    public int Priority => 100;

    public ContentMetadataContribution Inspect(ContentInspectionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var item = context.Item;
        var hasLocalPath = !string.IsNullOrWhiteSpace(item.SourcePath) &&
                           item.Kind is DropItemKind.File or DropItemKind.Folder or DropItemKind.Image;
        var hasOriginalPath = hasLocalPath && item.Backing.Kind == ContentBackingKind.OriginalPath;
        var isTableShapedCapture = ContentDetection.ContainsHtmlTable(item.Html);
        var hasImageFile = item.Kind == DropItemKind.Image && hasLocalPath && !isTableShapedCapture;
        var representations = GetRepresentations(item, hasLocalPath, hasImageFile, isTableShapedCapture);
        return new ContentMetadataContribution
        {
            Representations = representations,
            Actions = GetActions(item, representations, hasLocalPath, hasOriginalPath),
            HasLocalPath = hasLocalPath,
            HasOriginalPath = hasOriginalPath,
            HasImageFile = hasImageFile,
            HasFile = item.Kind == DropItemKind.File,
            HasFolder = item.Kind == DropItemKind.Folder
        };
    }

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

        if (representations.HasFlag(ContentRepresentations.WebLink) ||
            representations.HasFlag(ContentRepresentations.ApplicationLink))
        {
            actions |= ContentActions.Open;
        }

        if (hasOriginalPath && representations.HasFlag(ContentRepresentations.StorageItem))
        {
            actions |= ContentActions.Cut | ContentActions.Delete;
        }

        if (ContentDetection.TryNormalizeWebUrl(item.SourceUrl, out _) ||
            TryNormalizeAbsoluteUri(item.SourceApplicationLink, out _))
        {
            actions |= ContentActions.OpenSource;
        }

        return actions;
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
}
