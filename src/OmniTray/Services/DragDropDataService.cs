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

    public const string StackReferenceFormat = "application/x-omnitray-stack-id";
    public const string ItemReferenceFormat = "application/x-omnitray-item-reference";

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

    public static async Task<IReadOnlyList<DropItem>> ReadAsync(DataPackageView dataView)
    {
        if (HasStackReference(dataView) || HasItemReference(dataView))
        {
            // Private OmniTray identity takes precedence over the public formats projected for
            // external applications. Never re-import those projections as new stack items.
            MarkActiveDragHandledInternally();
            return [];
        }

        var representations = await ReadRepresentationsAsync(dataView);

        if (dataView.Contains(StandardDataFormats.StorageItems))
        {
            var storageItems = await dataView.GetStorageItemsAsync();
            var capturedItems = await ReadStorageItemsAsync(storageItems, representations);
            if (capturedItems.Count > 0)
            {
                return AttachCustomFormats(
                    DropImportDeduplication.FilterNewItems([], capturedItems),
                    representations);
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
            var bitmapReference = await dataView.GetBitmapAsync();
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
        var enrichedItems = representations.ApplicationLink is null
            ? items
            : items.Select(item => item.WithRepresentations(
                    applicationLink: representations.ApplicationLink.AbsoluteUri))
                .ToArray();
        if (enrichedItems.Count != 1 || representations.CustomFormats.Count == 0)
        {
            return enrichedItems;
        }

        return [enrichedItems[0].WithCustomFormats(representations.CustomFormats)];
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

        var isImage = file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(file.Path))
        {
            return isImage
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
        }

        return isImage
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
    }

    private static async Task<CapturedRepresentations> ReadRepresentationsAsync(DataPackageView dataView)
    {
        string? text = null;
        string? html = null;
        string? rtf = null;
        Uri? webLink = null;
        Uri? applicationLink = null;

        if (dataView.Contains(StandardDataFormats.Text))
        {
            try
            {
                text = await dataView.GetTextAsync();
            }
            catch
            {
                // Another advertised representation can still be captured.
            }
        }

        if (dataView.Contains(StandardDataFormats.Html))
        {
            try
            {
                html = await dataView.GetHtmlFormatAsync();
            }
            catch
            {
                // Another advertised representation can still be captured.
            }
        }

        if (dataView.Contains(StandardDataFormats.Rtf))
        {
            try
            {
                rtf = await dataView.GetRtfAsync();
            }
            catch
            {
                // Another advertised representation can still be captured.
            }
        }

        if (dataView.Contains(StandardDataFormats.WebLink))
        {
            try
            {
                webLink = await dataView.GetWebLinkAsync();
            }
            catch
            {
                // Text detection remains available when a link provider fails.
            }
        }

        if (dataView.Contains(StandardDataFormats.ApplicationLink))
        {
            try
            {
                applicationLink = await dataView.GetApplicationLinkAsync();
            }
            catch
            {
                // Another advertised representation can still be captured.
            }
        }

        var sourceUrl = dataView.Properties.ContentSourceWebLink?.AbsoluteUri ??
                        ContentDetection.ExtractSourceUrlFromHtml(html) ??
                        webLink?.AbsoluteUri;
        var customFormats = await ReadCustomFormatsAsync(dataView);
        return new CapturedRepresentations(
            NormalizeOptional(text),
            NormalizeOptional(html),
            NormalizeOptional(rtf),
            webLink,
            applicationLink,
            sourceUrl,
            NormalizeOptional(dataView.Properties.ApplicationName),
            customFormats);
    }

    private static async Task<IReadOnlyList<DropItemDataFormat>> ReadCustomFormatsAsync(
        DataPackageView dataView)
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
                            continue;
                        }

                        formats.Add(DropItemDataFormat.CreateText(formatId, text));
                        totalBytes += byteCount;
                        break;
                    }
                    case IRandomAccessStreamReference streamReference:
                    {
                        using var stream = await streamReference.OpenReadAsync();
                        if (await ReadCustomFormatBytesAsync(stream, MaxCustomFormatTotalBytes - totalBytes)
                            is not { } bytes)
                        {
                            continue;
                        }

                        formats.Add(DropItemDataFormat.CreateBinary(formatId, bytes));
                        totalBytes += (ulong)bytes.Length;
                        break;
                    }
                    case IRandomAccessStream stream:
                    {
                        if (await ReadCustomFormatBytesAsync(stream, MaxCustomFormatTotalBytes - totalBytes)
                            is not { } bytes)
                        {
                            continue;
                        }

                        formats.Add(DropItemDataFormat.CreateBinary(formatId, bytes));
                        totalBytes += (ulong)bytes.Length;
                        break;
                    }
                    case IBuffer buffer:
                    {
                        if (buffer.Length > MaxCustomFormatBytes ||
                            buffer.Length > MaxCustomFormatTotalBytes - totalBytes)
                        {
                            continue;
                        }

                        using var reader = DataReader.FromBuffer(buffer);
                        var bytes = new byte[buffer.Length];
                        reader.ReadBytes(bytes);
                        formats.Add(DropItemDataFormat.CreateBinary(formatId, bytes));
                        totalBytes += buffer.Length;
                        break;
                    }
                }
            }
            catch
            {
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
    IReadOnlyList<DropItemDataFormat> CustomFormats)
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
        []);
}
