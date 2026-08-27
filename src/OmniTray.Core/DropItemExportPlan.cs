// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

namespace OmniTray.Core;

public sealed record DropItemExportPlan(
    string? Text,
    string? Html,
    string? Rtf,
    string? Url,
    string? ApplicationLink,
    string? SourceUrl,
    string? SourceApplicationName,
    string? SourcePackageFamilyName,
    string? SourceApplicationLink,
    IReadOnlyList<DropItemHtmlResource> HtmlResources,
    IReadOnlyList<DropItemDataFormat> CustomFormats,
    bool IncludesStorageItems,
    bool IncludesBitmap)
{
    public static DropItemExportPlan Create(IReadOnlyList<DropItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        var text = string.Join(
            Environment.NewLine,
            items
                .Where(static item => item.Kind is DropItemKind.Text or DropItemKind.Uri)
                .Select(static item => item.Text)
                .Where(static value => !string.IsNullOrEmpty(value)));
        var singleItem = items.Count == 1 ? items[0] : null;
        var containsHtmlTable = ContentDetection.ContainsHtmlTable(singleItem?.Html);
        if (containsHtmlTable && !string.IsNullOrEmpty(singleItem?.Text))
        {
            text = singleItem.Text;
        }

        return new DropItemExportPlan(
            string.IsNullOrEmpty(text) ? null : text,
            singleItem?.Html,
            singleItem?.Rtf,
            singleItem?.Url,
            singleItem?.ApplicationLink,
            singleItem?.SourceUrl,
            singleItem?.SourceApplicationName,
            singleItem?.SourcePackageFamilyName,
            singleItem?.SourceApplicationLink,
            singleItem?.HtmlResources ?? [],
            singleItem?.CustomFormats ?? [],
            items.Any(CanExportAsStorageItem),
            singleItem?.Kind == DropItemKind.Image && !containsHtmlTable);
    }

    private static bool CanExportAsStorageItem(DropItem item) =>
        item.Kind is DropItemKind.File or DropItemKind.Folder ||
        (item.Kind == DropItemKind.Image && !ContentDetection.ContainsHtmlTable(item.Html));
}
