// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.System;
using Windows.UI.Core;
using Microsoft.UI.Input;

namespace OmniTray.Services;

internal static class DragDropDataService
{
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
          dataView.Contains(StandardDataFormats.Text)));

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

        if (dataView.Contains(StandardDataFormats.StorageItems))
        {
            var storageItems = await dataView.GetStorageItemsAsync();
            var capturedItems = await ReadStorageItemsAsync(storageItems);
            if (capturedItems.Count > 0)
            {
                return DropImportDeduplication.FilterNewItems([], capturedItems);
            }
        }

        if (dataView.Contains(StandardDataFormats.Bitmap))
        {
            var bitmapReference = await dataView.GetBitmapAsync();
            return [await ContentStore.MaterializeBitmapAsync(bitmapReference)];
        }

        if (dataView.Contains(StandardDataFormats.Text))
        {
            var text = await dataView.GetTextAsync();
            return string.IsNullOrWhiteSpace(text)
                ? []
                : [await ContentStore.MaterializeTextAsync(text)];
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
        string title)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        WriteContent(data, items, title);
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

        var text = string.Join(
            Environment.NewLine,
            items
                .Where(static item => item.Kind == DropItemKind.Text)
                .Select(static item => item.Text)
                .Where(static value => !string.IsNullOrEmpty(value)));
        if (!string.IsNullOrEmpty(text))
        {
            data.SetText(text);
        }

        if (items.Any(static item => !string.IsNullOrWhiteSpace(item.SourcePath)))
        {
            data.SetDataProvider(
                StandardDataFormats.StorageItems,
                request => ProvideStorageItems(request, items));
        }

        if (items.Count == 1 && items[0].Kind == DropItemKind.Image)
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

    private static async Task<IReadOnlyList<DropItem>> ReadStorageItemsAsync(
        IReadOnlyList<IStorageItem> storageItems)
    {
        var capturedItems = new List<DropItem>(storageItems.Count);
        foreach (var item in storageItems)
        {
            try
            {
                var capturedItem = await CreateDropItemAsync(item);
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

    private static async Task<DropItem?> CreateDropItemAsync(IStorageItem item)
    {
        if (item is StorageFolder folder)
        {
            return string.IsNullOrWhiteSpace(folder.Path)
                ? null
                : DropItem.CreateStorageItem(folder.Name, folder.Path, true);
        }

        if (item is not StorageFile file)
        {
            return null;
        }

        var isImage = file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(file.Path))
        {
            return isImage
                ? DropItem.CreateImage(file.Name, file.Path)
                : DropItem.CreateStorageItem(file.Name, file.Path, false);
        }

        return isImage
            ? await ContentStore.MaterializeImageFileAsync(file)
            : await ContentStore.MaterializeVirtualFileAsync(file);
    }

    private static async void ProvideStorageItems(DataProviderRequest request, IReadOnlyList<DropItem> items)
    {
        var deferral = request.GetDeferral();
        try
        {
            var storageItems = new List<IStorageItem>();
            foreach (var item in items.Where(static item => !string.IsNullOrWhiteSpace(item.SourcePath)))
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
}

internal sealed record ItemDragReference(Guid SourceStackId, IReadOnlyList<Guid> ItemIds);
