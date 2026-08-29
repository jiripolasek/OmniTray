// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

using System.Text;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.System;
using Windows.UI.Core;
using Microsoft.UI.Input;

namespace OmniTray.Services;

internal static class DragDropDataService
{
    private const int MaxCustomFormatCount = 32;
    private const ulong MaxCustomFormatBytes = 8UL * 1024 * 1024;
    private const ulong MaxCustomFormatTotalBytes = 32UL * 1024 * 1024;

    public const string StackReferenceFormat = "application/x-omnitray-stack-id";
    public const string ItemReferenceFormat = "application/x-omnitray-item-reference";

    private static readonly HashSet<string> StandardFormatIds = new(StringComparer.Ordinal)
    {
        StandardDataFormats.ApplicationLink,
        StandardDataFormats.Bitmap,
        StandardDataFormats.Html,
        StandardDataFormats.Rtf,
        StandardDataFormats.StorageItems,
        StandardDataFormats.Text,
        StandardDataFormats.Uri,
        StandardDataFormats.WebLink
    };

    public static Guid? ActiveStackReferenceId { get; private set; }

    public static ItemDragReference? ActiveItemReference { get; private set; }

    public static bool ActiveExternalMoveRequested { get; private set; }

    public static bool HasActiveDrag =>
        ActiveStackReferenceId is not null || ActiveItemReference is not null;

    public static bool HasSupportedFormat(DataPackageView dataView) =>
        HasItemReference(dataView) ||
        (!HasStackReference(dataView) &&
         (dataView.Contains(StandardDataFormats.StorageItems) ||
          dataView.Contains(StandardDataFormats.Bitmap) ||
          dataView.Contains(StandardDataFormats.Text) ||
          dataView.Contains(StandardDataFormats.Html) ||
          dataView.Contains(StandardDataFormats.Rtf) ||
          dataView.Contains(StandardDataFormats.Uri) ||
          dataView.Contains(StandardDataFormats.WebLink) ||
          dataView.Contains(StandardDataFormats.ApplicationLink)));

    public static bool HasStackReference(DataPackageView dataView) =>
        dataView.Contains(StackReferenceFormat);

    public static bool HasItemReference(DataPackageView dataView) =>
        dataView.Contains(ItemReferenceFormat);

    public static DataPackageOperation GetAcceptedInternalMoveOperation(DataPackageView dataView)
    {
        ArgumentNullException.ThrowIfNull(dataView);
        return (dataView.RequestedOperation & DataPackageOperation.Move) != 0
            ? DataPackageOperation.Move
            : DataPackageOperation.Copy;
    }

    public static async Task<Guid?> ReadStackReferenceAsync(DataPackageView dataView)
    {
        ArgumentNullException.ThrowIfNull(dataView);
        if (!HasStackReference(dataView))
        {
            return null;
        }

        MarkActiveDragHandledInternally();
        var value = await dataView.GetDataAsync(StackReferenceFormat);
        return value is string text && Guid.TryParse(text, out var stackId)
            ? stackId
            : null;
    }

    public static async Task<ItemDragReference?> ReadItemReferenceAsync(DataPackageView dataView)
    {
        ArgumentNullException.ThrowIfNull(dataView);
        if (!HasItemReference(dataView))
        {
            return null;
        }

        MarkActiveDragHandledInternally();
        var value = await dataView.GetDataAsync(ItemReferenceFormat);
        if (value is not string text)
        {
            return null;
        }

        var parts = text.Split('|', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || !Guid.TryParse(parts[0], out var sourceStackId))
        {
            return null;
        }

        var itemIds = parts[1]
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static value => Guid.TryParse(value, out var itemId) ? itemId : Guid.Empty)
            .ToArray();
        return sourceStackId == Guid.Empty ||
               itemIds.Length == 0 ||
               itemIds.Any(static id => id == Guid.Empty) ||
               itemIds.Distinct().Count() != itemIds.Length
            ? null
            : new ItemDragReference(sourceStackId, itemIds);
    }

    public static async Task<IReadOnlyList<DropItem>> ReadAsync(
        DataPackageView dataView,
        CaptureChannel channel = CaptureChannel.Drag)
    {
        if (HasStackReference(dataView) || HasItemReference(dataView))
        {
            // Private OmniTray identity takes precedence over the public formats projected for
            // external applications. Never re-import those projections as new stack items.
            MarkActiveDragHandledInternally();
            return [];
        }

        var representations = await ReadRepresentationsAsync(dataView, channel);

        if (dataView.Contains(StandardDataFormats.StorageItems))
        {
            try
            {
                var storageItems = await dataView.GetStorageItemsAsync();
                representations.Inventory.MarkSucceeded(
                    StandardDataFormats.StorageItems,
                    $"{storageItems.Count:N0} storage item{(storageItems.Count == 1 ? string.Empty : "s")}");
                var capturedItems = await ReadStorageItemsAsync(storageItems, representations);
                if (capturedItems.Count > 0)
                {
                    var filteredItems = DropImportDeduplication.FilterNewItems([], capturedItems);
                    if (filteredItems.Count != 1 && representations.HtmlResources.Count > 0)
                    {
                        await ContentStore.DeleteHtmlResourcesAsync(representations.HtmlResources);
                        representations = representations with { HtmlResources = [] };
                    }

                    return AttachCustomFormats(filteredItems, representations);
                }
            }
            catch (Exception exception)
            {
                representations.Inventory.MarkFailed(StandardDataFormats.StorageItems, exception);
            }
        }

        if (ContentDetection.ContainsHtmlTable(representations.Html))
        {
            return AttachCustomFormats(
            [
                await ContentStore.MaterializeTextAsync(
                    representations.Text,
                    representations.Html,
                    representations.Rtf,
                    representations.SourceUrl,
                    representations.SourceApplicationName)
            ], representations);
        }

        if (dataView.Contains(StandardDataFormats.Bitmap))
        {
            try
            {
                var bitmapReference = await dataView.GetBitmapAsync();
                representations.Inventory.MarkSucceeded(StandardDataFormats.Bitmap, "Bitmap stream reference");
                return AttachCustomFormats(
                [
                    await ContentStore.MaterializeBitmapAsync(
                        bitmapReference,
                        "Dropped image",
                        representations.Text,
                        representations.Html,
                        representations.Rtf,
                        representations.SourceUrl,
                        representations.SourceApplicationName)
                ], representations);
            }
            catch (Exception exception)
            {
                representations.Inventory.MarkFailed(StandardDataFormats.Bitmap, exception);
            }
        }

        if (representations.WebLink is { } webLink)
        {
            return AttachCustomFormats(
            [
                DropItem.CreateUri(
                    webLink.AbsoluteUri,
                    representations.Text,
                    representations.Text,
                    representations.Html,
                    representations.Rtf,
                    representations.SourceUrl,
                    representations.SourceApplicationName)
            ], representations);
        }

        if (ContentDetection.TryNormalizeWebUrl(representations.Text, out var detectedUrl))
        {
            return AttachCustomFormats(
            [
                DropItem.CreateUri(
                    detectedUrl,
                    text: representations.Text,
                    html: representations.Html,
                    rtf: representations.Rtf,
                    sourceUrl: representations.SourceUrl,
                    sourceApplicationName: representations.SourceApplicationName)
            ], representations);
        }

        if (representations.ApplicationLink is { } applicationLink)
        {
            return AttachCustomFormats(
            [
                ContentDetection.TryNormalizeWebUrl(applicationLink.AbsoluteUri, out var applicationWebUrl)
                    ? DropItem.CreateUri(
                        applicationWebUrl,
                        text: representations.Text,
                        html: representations.Html,
                        rtf: representations.Rtf,
                        sourceUrl: representations.SourceUrl,
                        sourceApplicationName: representations.SourceApplicationName,
                        applicationLink: applicationLink.AbsoluteUri)
                    : DropItem.CreateText(
                        representations.Text ?? applicationLink.AbsoluteUri,
                        html: representations.Html,
                        rtf: representations.Rtf,
                        sourceUrl: representations.SourceUrl,
                        sourceApplicationName: representations.SourceApplicationName,
                        applicationLink: applicationLink.AbsoluteUri)
            ], representations);
        }

        if (representations.HasTextContent)
        {
            return AttachCustomFormats(
            [
                await ContentStore.MaterializeTextAsync(
                    representations.Text,
                    representations.Html,
                    representations.Rtf,
                    representations.SourceUrl,
                    representations.SourceApplicationName)
            ], representations);
        }

        if (representations.HtmlResources.Count > 0)
        {
            await ContentStore.DeleteHtmlResourcesAsync(representations.HtmlResources);
        }

        return [];
    }

    public static void Write(
        DataPackage data,
        DropStack stack,
        string title,
        bool allowMoveOnDragOut)
    {
        ArgumentNullException.ThrowIfNull(stack);
        WriteContent(data, stack.Items, title);
        ActiveItemReference = null;
        ActiveStackReferenceId = stack.Id;
        ConfigureRequestedOperation(data, stack.Items, allowMoveOnDragOut);
        data.SetData(StackReferenceFormat, stack.Id.ToString("D"));
    }

    public static void WriteStandardContent(
        DataPackage data,
        IReadOnlyList<DropItem> items,
        string title,
        DataPackageOperation requestedOperation = DataPackageOperation.Copy)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        WriteContent(data, items, title);
        data.RequestedOperation = requestedOperation;
    }

    private static void WriteContent(
        DataPackage data,
        IReadOnlyList<DropItem> items,
        string title)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(items);

        data.RequestedOperation = DataPackageOperation.Copy;
        data.Properties.Title = title;

        var exportPlan = DropItemExportPlan.Create(items);
        if (exportPlan.Text is not null)
        {
            data.SetText(exportPlan.Text);
        }

        if (exportPlan.Html is not null)
        {
            data.SetHtmlFormat(exportPlan.Html);
        }

        if (exportPlan.Rtf is not null)
        {
            data.SetRtf(exportPlan.Rtf);
        }

        if (ContentDetection.TryNormalizeWebUrl(exportPlan.Url, out var url))
        {
            data.SetWebLink(new Uri(url));
        }

        if (Uri.TryCreate(exportPlan.ApplicationLink, UriKind.Absolute, out var applicationLink))
        {
            data.SetApplicationLink(applicationLink);
        }

        if (ContentDetection.TryNormalizeWebUrl(exportPlan.SourceUrl, out var sourceUrl))
        {
            data.Properties.ContentSourceWebLink = new Uri(sourceUrl);
        }

        if (!string.IsNullOrWhiteSpace(exportPlan.SourceApplicationName))
        {
            try
            {
                data.Properties.ApplicationName = exportPlan.SourceApplicationName;
            }
            catch
            {
                // Invalid attribution metadata must not break the reusable payload.
            }
        }

        if (!string.IsNullOrWhiteSpace(exportPlan.SourcePackageFamilyName))
        {
            try
            {
                data.Properties.PackageFamilyName = exportPlan.SourcePackageFamilyName;
            }
            catch
            {
                // Invalid attribution metadata must not break the reusable payload.
            }
        }

        if (Uri.TryCreate(exportPlan.SourceApplicationLink, UriKind.Absolute, out var sourceApplicationLink))
        {
            try
            {
                data.Properties.ContentSourceApplicationLink = sourceApplicationLink;
            }
            catch
            {
                // Invalid attribution metadata must not break the reusable payload.
            }
        }

        foreach (var resource in exportPlan.HtmlResources)
        {
            try
            {
                data.ResourceMap[resource.ResourceKey] = RandomAccessStreamReference.CreateFromUri(
                    ContentStore.CreateHtmlResourceUri(resource));
            }
            catch
            {
                // A missing managed resource must not break the remaining representations.
            }
        }

        foreach (var format in exportPlan.CustomFormats)
        {
            if (IsStandardFormat(format.FormatId) || IsPrivateFormat(format.FormatId))
            {
                continue;
            }

            try
            {
                if (format.Kind == DropItemDataFormatKind.Text)
                {
                    data.SetData(format.FormatId, format.Text!);
                }
                else
                {
                    data.SetDataProvider(
                        format.FormatId,
                        request => ProvideCustomFormat(request, format));
                }
            }
            catch
            {
                // An invalid or unsupported custom identifier must not break standard drag-out.
            }
        }

        if (exportPlan.IncludesStorageItems)
        {
            data.SetDataProvider(
                StandardDataFormats.StorageItems,
                request => ProvideStorageItems(request, items));
        }

        if (exportPlan.IncludesBitmap)
        {
            data.SetDataProvider(
                StandardDataFormats.Bitmap,
                request => ProvideBitmap(request, items[0]));
        }
    }

    public static void WriteItems(
        DataPackage data,
        Guid sourceStackId,
        IReadOnlyList<DropItem> items,
        string title,
        bool allowMoveOnDragOut)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(items);
        if (sourceStackId == Guid.Empty)
        {
            throw new ArgumentException("A source stack ID is required.", nameof(sourceStackId));
        }

        if (items.Count == 0)
        {
            throw new ArgumentException("At least one item is required.", nameof(items));
        }

        WriteContent(data, items, title);
        ActiveStackReferenceId = null;
        ActiveItemReference = new ItemDragReference(
            sourceStackId,
            items.Select(static item => item.Id).ToArray());
        ConfigureRequestedOperation(data, items, allowMoveOnDragOut);
        data.SetData(
            ItemReferenceFormat,
            $"{sourceStackId:D}|{string.Join(',', items.Select(static item => item.Id.ToString("D")))}");
    }

    public static Guid? CompleteStackDrag(DataPackageOperation dropResult)
    {
        var stackId = DragOutPolicy.ShouldRemoveSource(
            ActiveExternalMoveRequested,
            dropResult == DataPackageOperation.Move)
            ? ActiveStackReferenceId
            : null;
        ClearActiveStackReference();
        return stackId;
    }

    public static ItemDragReference? CompleteItemDrag(DataPackageOperation dropResult)
    {
        var itemReference = DragOutPolicy.ShouldRemoveSource(
            ActiveExternalMoveRequested,
            dropResult == DataPackageOperation.Move)
            ? ActiveItemReference
            : null;
        ClearActiveItemReference();
        return itemReference;
    }

    public static void ClearActiveStackReference()
    {
        ActiveStackReferenceId = null;
        ActiveExternalMoveRequested = false;
    }

    public static void ClearActiveItemReference()
    {
        ActiveItemReference = null;
        ActiveExternalMoveRequested = false;
    }

    private static void ConfigureRequestedOperation(
        DataPackage data,
        IReadOnlyList<DropItem> items,
        bool allowMoveOnDragOut)
    {
        ActiveExternalMoveRequested = DragOutPolicy.ShouldRequestMove(
            allowMoveOnDragOut,
            IsKeyDown(VirtualKey.Shift),
            IsKeyDown(VirtualKey.Control),
            items);
        data.RequestedOperation = ActiveExternalMoveRequested
            ? DataPackageOperation.Copy | DataPackageOperation.Move
            : DataPackageOperation.Copy;
    }

    private static bool IsKeyDown(VirtualKey key) =>
        (InputKeyboardSource.GetKeyStateForCurrentThread(key) & CoreVirtualKeyStates.Down) != 0;

    private static void MarkActiveDragHandledInternally()
    {
        // OmniTray owns source changes for its private reference formats. The drag result must
        // not also trigger the cleanup reserved for a completed external filesystem move.
        ActiveExternalMoveRequested = false;
    }

    private static IReadOnlyList<DropItem> AttachCustomFormats(
        IReadOnlyList<DropItem> items,
        CapturedRepresentations representations)
    {
        var provenance = new ContentProvenance
        {
            ApplicationName = representations.SourceApplicationName,
            PackageFamilyName = representations.SourcePackageFamilyName,
            SourceWebLink = representations.SourceUrl,
            SourceApplicationLink = representations.SourceApplicationLink
        };
        var inventory = representations.Inventory.CreateSnapshot();
        var enrichedItems = items.Select((item, ordinal) =>
        {
            var enriched = representations.ApplicationLink is null
                ? item
                : item.WithRepresentations(
                    applicationLink: representations.ApplicationLink.AbsoluteUri);
            if (items.Count == 1 && representations.CustomFormats.Count > 0)
            {
                enriched = enriched.WithCustomFormats(representations.CustomFormats);
            }

            return enriched.WithMetadata(
                provenance,
                new DropCaptureMetadata
                {
                    CaptureId = representations.CaptureId,
                    Channel = representations.Channel,
                    CapturedAt = representations.CapturedAt,
                    Ordinal = ordinal,
                    RequestedOperation = representations.RequestedOperation,
                    Formats = inventory
                },
                htmlResources: items.Count == 1 ? representations.HtmlResources : []);
        }).ToArray();
        return enrichedItems;
    }

    private static async Task<IReadOnlyList<DropItem>> ReadStorageItemsAsync(
        IReadOnlyList<IStorageItem> storageItems,
        CapturedRepresentations representations)
    {
        var capturedItems = new List<DropItem>(storageItems.Count);
        foreach (var item in storageItems)
        {
            try
            {
                var capturedItem = await CreateDropItemAsync(
                    item,
                    storageItems.Count == 1 ? representations : representations.ProvenanceOnly());
                if (capturedItem is not null)
                {
                    capturedItems.Add(capturedItem);
                }
            }
            catch
            {
                // A pathless virtual item may still be available through another advertised format.
            }
        }

        return capturedItems;
    }

    private static async Task<DropItem?> CreateDropItemAsync(
        IStorageItem item,
        CapturedRepresentations representations)
    {
        if (item is StorageFolder folder)
        {
            return string.IsNullOrWhiteSpace(folder.Path)
                ? null
                : DropItem.CreateStorageItem(folder.Name, folder.Path, true).WithRepresentations(
                    representations.Text,
                    representations.Html,
                    representations.Rtf,
                    representations.SourceUrl,
                    representations.SourceApplicationName);
        }

        if (item is not StorageFile file)
        {
            return null;
        }

        var fileFacts = await ReadFileFactsAsync(file);
        var isImage = fileFacts.ContentType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true;
        if (!string.IsNullOrWhiteSpace(file.Path))
        {
            var captured = isImage
                ? DropItem.CreateImage(
                    file.Name,
                    file.Path,
                    false,
                    representations.Text,
                    representations.Html,
                    representations.Rtf,
                    representations.SourceUrl,
                    representations.SourceApplicationName)
                : DropItem.CreateStorageItem(file.Name, file.Path, false).WithRepresentations(
                    representations.Text,
                    representations.Html,
                    representations.Rtf,
                    representations.SourceUrl,
                    representations.SourceApplicationName);
            return captured.WithMetadata(fileFacts: fileFacts);
        }

        var materialized = isImage
            ? await ContentStore.MaterializeImageFileAsync(
                file,
                representations.Text,
                representations.Html,
                representations.Rtf,
                representations.SourceUrl,
                representations.SourceApplicationName)
            : await ContentStore.MaterializeVirtualFileAsync(
                file,
                representations.Text,
                representations.Html,
                representations.Rtf,
                representations.SourceUrl,
                representations.SourceApplicationName);
        return materialized.WithMetadata(
            backing: new ContentBacking
            {
                Kind = ContentBackingKind.VirtualFileMaterialization, Path = materialized.SourcePath
            },
            fileFacts: fileFacts);
    }

    private static async Task<DropFileFacts> ReadFileFactsAsync(StorageFile file)
    {
        var fileFacts = new DropFileFacts { OriginalFileName = file.Name };
        try
        {
            fileFacts = fileFacts with { ContentType = NormalizeOptional(file.ContentType) };
            var properties = await file.GetBasicPropertiesAsync();
            return fileFacts with { Size = properties.Size, ModifiedAt = properties.DateModified };
        }
        catch
        {
            // File facts are optional metadata. An unavailable provider must not reject the file.
            return fileFacts;
        }
    }

    private static async Task<CapturedRepresentations> ReadRepresentationsAsync(
        DataPackageView dataView,
        CaptureChannel channel)
    {
        var inventory = new FormatInventoryBuilder(dataView.AvailableFormats);
        var captureId = Guid.NewGuid();
        var capturedAt = DateTimeOffset.UtcNow;
        string? text = null;
        string? html = null;
        string? rtf = null;
        Uri? webLink = null;
        Uri? applicationLink = null;
        IReadOnlyList<DropItemHtmlResource> htmlResources = [];

        if (dataView.Contains(StandardDataFormats.Text))
        {
            try
            {
                text = await dataView.GetTextAsync();
                inventory.MarkSucceeded(StandardDataFormats.Text, $"{text.Length:N0} characters");
            }
            catch (Exception exception)
            {
                inventory.MarkFailed(StandardDataFormats.Text, exception);
                // Another advertised representation can still be captured.
            }
        }

        if (dataView.Contains(StandardDataFormats.Html))
        {
            try
            {
                html = await dataView.GetHtmlFormatAsync();
                inventory.MarkSucceeded(StandardDataFormats.Html, $"{html.Length:N0} characters");
            }
            catch (Exception exception)
            {
                inventory.MarkFailed(StandardDataFormats.Html, exception);
                // Another advertised representation can still be captured.
            }

            if (html is not null)
            {
                try
                {
                    var resourceMap = await dataView.GetResourceMapAsync();
                    htmlResources = await ContentStore.MaterializeHtmlResourcesAsync(resourceMap);
                    if (resourceMap.Count > 0)
                    {
                        inventory.MarkSucceeded(
                            StandardDataFormats.Html,
                            $"{html.Length:N0} characters · {htmlResources.Count:N0}/{resourceMap.Count:N0} resources saved");
                    }
                }
                catch
                {
                    // The HTML representation remains useful without its optional resource map.
                }
            }
        }

        if (dataView.Contains(StandardDataFormats.Rtf))
        {
            try
            {
                rtf = await dataView.GetRtfAsync();
                inventory.MarkSucceeded(StandardDataFormats.Rtf, $"{rtf.Length:N0} characters");
            }
            catch (Exception exception)
            {
                inventory.MarkFailed(StandardDataFormats.Rtf, exception);
                // Another advertised representation can still be captured.
            }
        }

        if (dataView.Contains(StandardDataFormats.WebLink))
        {
            try
            {
                webLink = await dataView.GetWebLinkAsync();
                inventory.MarkSucceeded(StandardDataFormats.WebLink, webLink.AbsoluteUri);
            }
            catch (Exception exception)
            {
                inventory.MarkFailed(StandardDataFormats.WebLink, exception);
                // Text detection remains available when a link provider fails.
            }
        }

        if (dataView.Contains(StandardDataFormats.ApplicationLink))
        {
            try
            {
                applicationLink = await dataView.GetApplicationLinkAsync();
                inventory.MarkSucceeded(StandardDataFormats.ApplicationLink, applicationLink.AbsoluteUri);
            }
            catch (Exception exception)
            {
                inventory.MarkFailed(StandardDataFormats.ApplicationLink, exception);
                // Another advertised representation can still be captured.
            }
        }

        if (dataView.Contains(StandardDataFormats.Uri))
        {
            try
            {
                var value = await dataView.GetDataAsync(StandardDataFormats.Uri);
                var legacyUri = value switch
                {
                    Uri uri => uri,
                    string uriText when Uri.TryCreate(uriText, UriKind.Absolute, out var uri) => uri,
                    _ => throw new InvalidOperationException("The URI format did not contain a URI value.")
                };
                inventory.MarkSucceeded(StandardDataFormats.Uri, legacyUri.AbsoluteUri);
                if (webLink is null &&
                    ContentDetection.TryNormalizeWebUrl(legacyUri.AbsoluteUri, out var normalizedWebLink))
                {
                    webLink = new Uri(normalizedWebLink);
                }
                else if (applicationLink is null &&
                         ContentDetection.TryNormalizeApplicationLink(
                             legacyUri.AbsoluteUri,
                             out var normalizedApplicationLink))
                {
                    applicationLink = new Uri(normalizedApplicationLink);
                }
            }
            catch (Exception exception)
            {
                inventory.MarkFailed(StandardDataFormats.Uri, exception);
                // Modern link formats and text/HTML detection remain available.
            }
        }

        if (applicationLink is null)
        {
            var detectedApplicationLink =
                ContentDetection.TryNormalizeApplicationLink(text, out var textApplicationLink)
                    ? textApplicationLink
                    : ContentDetection.ExtractApplicationLinkFromHtml(html);
            if (detectedApplicationLink is not null)
            {
                applicationLink = new Uri(detectedApplicationLink);
            }
        }

        var sourceUrl = dataView.Properties.ContentSourceWebLink?.AbsoluteUri ??
                        ContentDetection.ExtractSourceUrlFromHtml(html) ??
                        webLink?.AbsoluteUri;
        var customFormats = await ReadCustomFormatsAsync(dataView, inventory);
        return new CapturedRepresentations(
            NormalizeOptional(text),
            NormalizeOptional(html),
            NormalizeOptional(rtf),
            webLink,
            applicationLink,
            sourceUrl,
            NormalizeOptional(dataView.Properties.ApplicationName),
            NormalizeOptional(dataView.Properties.PackageFamilyName),
            dataView.Properties.ContentSourceApplicationLink?.AbsoluteUri,
            customFormats,
            htmlResources,
            captureId,
            channel,
            capturedAt,
            ConvertRequestedOperation(dataView.RequestedOperation),
            inventory);
    }

    private static async Task<IReadOnlyList<DropItemDataFormat>> ReadCustomFormatsAsync(
        DataPackageView dataView,
        FormatInventoryBuilder inventory)
    {
        var formats = new List<DropItemDataFormat>();
        var capturedFormatIds = new HashSet<string>(StringComparer.Ordinal);
        ulong totalBytes = 0;
        foreach (var formatId in dataView.AvailableFormats)
        {
            if (formats.Count >= MaxCustomFormatCount ||
                IsStandardFormat(formatId) ||
                IsPrivateFormat(formatId) ||
                !capturedFormatIds.Add(formatId))
            {
                if (formats.Count >= MaxCustomFormatCount &&
                    !IsStandardFormat(formatId) &&
                    !IsPrivateFormat(formatId))
                {
                    inventory.MarkSkipped(formatId, "Custom-format count limit reached");
                }

                continue;
            }

            try
            {
                var value = await dataView.GetDataAsync(formatId);
                switch (value)
                {
                    case string text:
                        {
                            var byteCount = (ulong)Encoding.UTF8.GetByteCount(text);
                            if (byteCount > MaxCustomFormatBytes ||
                                byteCount > MaxCustomFormatTotalBytes - totalBytes)
                            {
                                inventory.MarkSkipped(formatId, "Custom-format size limit exceeded");
                                continue;
                            }

                            formats.Add(DropItemDataFormat.CreateText(formatId, text));
                            totalBytes += byteCount;
                            inventory.MarkSucceeded(formatId, $"{byteCount:N0} UTF-8 bytes");
                            break;
                        }
                    case IRandomAccessStreamReference streamReference:
                        {
                            using var stream = await streamReference.OpenReadAsync();
                            if (await ReadCustomFormatBytesAsync(stream, MaxCustomFormatTotalBytes - totalBytes)
                                is not { } bytes)
                            {
                                inventory.MarkSkipped(formatId, "Custom-format size limit exceeded");
                                continue;
                            }

                            formats.Add(DropItemDataFormat.CreateBinary(formatId, bytes));
                            totalBytes += (ulong)bytes.Length;
                            inventory.MarkSucceeded(formatId, $"{bytes.Length:N0} bytes");
                            break;
                        }
                    case IRandomAccessStream stream:
                        {
                            if (await ReadCustomFormatBytesAsync(stream, MaxCustomFormatTotalBytes - totalBytes)
                                is not { } bytes)
                            {
                                inventory.MarkSkipped(formatId, "Custom-format size limit exceeded");
                                continue;
                            }

                            formats.Add(DropItemDataFormat.CreateBinary(formatId, bytes));
                            totalBytes += (ulong)bytes.Length;
                            inventory.MarkSucceeded(formatId, $"{bytes.Length:N0} bytes");
                            break;
                        }
                    case IBuffer buffer:
                        {
                            if (buffer.Length > MaxCustomFormatBytes ||
                                buffer.Length > MaxCustomFormatTotalBytes - totalBytes)
                            {
                                inventory.MarkSkipped(formatId, "Custom-format size limit exceeded");
                                continue;
                            }

                            using var reader = DataReader.FromBuffer(buffer);
                            var bytes = new byte[buffer.Length];
                            reader.ReadBytes(bytes);
                            formats.Add(DropItemDataFormat.CreateBinary(formatId, bytes));
                            totalBytes += buffer.Length;
                            inventory.MarkSucceeded(formatId, $"{buffer.Length:N0} bytes");
                            break;
                        }
                    default:
                        inventory.MarkSkipped(
                            formatId,
                            value is null ? "Provider returned null" : value.GetType().Name);
                        break;
                }
            }
            catch (Exception exception)
            {
                inventory.MarkFailed(formatId, exception);
                // OLE formats that require a different TYMED remain visible in the inspector,
                // but cannot be safely projected into a reusable WinRT value here.
            }
        }

        return formats;
    }

    private static async Task<byte[]?> ReadCustomFormatBytesAsync(
        IRandomAccessStream stream,
        ulong remainingBytes)
    {
        if (stream.Size > MaxCustomFormatBytes ||
            stream.Size > remainingBytes ||
            stream.Size > uint.MaxValue)
        {
            return null;
        }

        using var input = stream.GetInputStreamAt(0);
        using var reader = new DataReader(input);
        var loaded = await reader.LoadAsync((uint)stream.Size);
        var bytes = new byte[loaded];
        reader.ReadBytes(bytes);
        return bytes;
    }

    private static bool IsStandardFormat(string formatId) => StandardFormatIds.Contains(formatId);

    private static bool IsPrivateFormat(string formatId) =>
        string.Equals(formatId, StackReferenceFormat, StringComparison.Ordinal) ||
        string.Equals(formatId, ItemReferenceFormat, StringComparison.Ordinal);

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private static CaptureRequestedOperation ConvertRequestedOperation(DataPackageOperation operation) =>
        (CaptureRequestedOperation)(int)operation;

    private static async void ProvideStorageItems(DataProviderRequest request, IReadOnlyList<DropItem> items)
    {
        var deferral = request.GetDeferral();
        try
        {
            var storageItems = new List<IStorageItem>();
            foreach (var item in items.Where(static item =>
                         !string.IsNullOrWhiteSpace(item.SourcePath) &&
                         (item.Kind is DropItemKind.File or DropItemKind.Folder ||
                          (item.Kind == DropItemKind.Image && !ContentDetection.ContainsHtmlTable(item.Html)))))
            {
                var storageItem = item.Kind == DropItemKind.Folder
                    ? (IStorageItem)await StorageFolder.GetFolderFromPathAsync(item.SourcePath!)
                    : await StorageFile.GetFileFromPathAsync(item.SourcePath!);
                storageItems.Add(storageItem);
            }

            request.SetData(storageItems);
        }
        catch
        {
            // The target treats an unfulfilled delayed format as an unavailable drag payload.
        }
        finally
        {
            deferral.Complete();
        }
    }

    private static async void ProvideBitmap(DataProviderRequest request, DropItem item)
    {
        var deferral = request.GetDeferral();
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(item.SourcePath!);
            request.SetData(RandomAccessStreamReference.CreateFromFile(file));
        }
        catch
        {
            // The target treats an unfulfilled delayed format as an unavailable drag payload.
        }
        finally
        {
            deferral.Complete();
        }
    }

    private static async void ProvideCustomFormat(
        DataProviderRequest request,
        DropItemDataFormat format)
    {
        var deferral = request.GetDeferral();
        try
        {
            var stream = new InMemoryRandomAccessStream();
            using var output = stream.GetOutputStreamAt(0);
            using var writer = new DataWriter(output);
            writer.WriteBytes(format.GetBinaryData());
            await writer.StoreAsync();
            await writer.FlushAsync();
            writer.DetachStream();
            stream.Seek(0);
            request.SetData(stream);
        }
        catch
        {
            // The target treats an unfulfilled delayed format as unavailable and can fall back.
        }
        finally
        {
            deferral.Complete();
        }
    }
}

internal sealed record ItemDragReference(Guid SourceStackId, IReadOnlyList<Guid> ItemIds);

internal sealed record CapturedRepresentations(
    string? Text,
    string? Html,
    string? Rtf,
    Uri? WebLink,
    Uri? ApplicationLink,
    string? SourceUrl,
    string? SourceApplicationName,
    string? SourcePackageFamilyName,
    string? SourceApplicationLink,
    IReadOnlyList<DropItemDataFormat> CustomFormats,
    IReadOnlyList<DropItemHtmlResource> HtmlResources,
    Guid CaptureId,
    CaptureChannel Channel,
    DateTimeOffset CapturedAt,
    CaptureRequestedOperation RequestedOperation,
    FormatInventoryBuilder Inventory)
{
    public bool HasTextContent =>
        !string.IsNullOrWhiteSpace(this.Text) ||
        !string.IsNullOrWhiteSpace(this.Html) ||
        !string.IsNullOrWhiteSpace(this.Rtf);

    public CapturedRepresentations ProvenanceOnly() => new(
        null,
        null,
        null,
        null,
        this.ApplicationLink,
        this.SourceUrl,
        this.SourceApplicationName,
        this.SourcePackageFamilyName,
        this.SourceApplicationLink,
        [],
        [],
        this.CaptureId,
        this.Channel,
        this.CapturedAt,
        this.RequestedOperation,
        this.Inventory);
}

internal sealed class FormatInventoryBuilder
{
    private readonly List<DataFormatInventoryEntry> _entries;

    public FormatInventoryBuilder(IEnumerable<string> formatIds)
    {
        this._entries = formatIds
            .Where(static formatId => !string.IsNullOrWhiteSpace(formatId))
            .Distinct(StringComparer.Ordinal)
            .Select(static formatId => new DataFormatInventoryEntry
            {
                FormatId = formatId, Status = DataFormatReadStatus.Advertised
            })
            .ToList();
    }

    public void MarkSucceeded(string formatId, string? detail = null) =>
        this.Set(formatId, DataFormatReadStatus.Succeeded, detail);

    public void MarkFailed(string formatId, Exception exception) =>
        this.Set(
            formatId,
            DataFormatReadStatus.Failed,
            $"{exception.GetType().Name} (0x{exception.HResult:X8}): {exception.Message}");

    public void MarkSkipped(string formatId, string detail) =>
        this.Set(formatId, DataFormatReadStatus.Skipped, detail);

    public IReadOnlyList<DataFormatInventoryEntry> CreateSnapshot() =>
        this._entries.ToArray();

    private void Set(string formatId, DataFormatReadStatus status, string? detail)
    {
        var index = this._entries.FindIndex(entry =>
            string.Equals(entry.FormatId, formatId, StringComparison.Ordinal));
        var entry = new DataFormatInventoryEntry
        {
            FormatId = formatId, Status = status, Detail = string.IsNullOrWhiteSpace(detail) ? null : detail.Trim()
        };
        if (index < 0)
        {
            this._entries.Add(entry);
        }
        else
        {
            this._entries[index] = entry;
        }
    }
}
