// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace OmniTray.Services;

internal static class ContentStore
{
    private const string ContentFolderName = "Content";

    public static async Task<DropItem> MaterializeTextAsync(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        var contentFolder = await GetContentFolderAsync();
        var file = await contentFolder.CreateFileAsync(
            $"text-{Guid.NewGuid():N}.txt",
            CreationCollisionOption.FailIfExists);
        await FileIO.WriteTextAsync(file, text);
        return DropItem.CreateText(text, file.Path, true);
    }

    public static async Task<DropItem> MaterializeBitmapAsync(
        RandomAccessStreamReference bitmapReference,
        string displayName = "Dropped image")
    {
        ArgumentNullException.ThrowIfNull(bitmapReference);

        using var input = await bitmapReference.OpenReadAsync();
        return await MaterializeImageStreamAsync(input, displayName);
    }

    public static async Task<DropItem> MaterializeImageFileAsync(StorageFile source)
    {
        ArgumentNullException.ThrowIfNull(source);

        using var input = await source.OpenReadAsync();
        var displayName = string.IsNullOrWhiteSpace(source.DisplayName)
            ? "Dropped image"
            : source.DisplayName;
        return await MaterializeImageStreamAsync(input, displayName);
    }

    public static async Task<DropItem> MaterializeVirtualFileAsync(StorageFile source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var contentFolder = await GetContentFolderAsync();
        var fileName = string.IsNullOrWhiteSpace(source.Name)
            ? $"drop-{Guid.NewGuid():N}"
            : source.Name;
        var copy = await source.CopyAsync(
            contentFolder,
            fileName,
            NameCollisionOption.GenerateUniqueName);

        return DropItem.CreateStorageItem(copy.Name, copy.Path, false, true);
    }

    public static async Task<IReadOnlyList<DropItem>> CopyItemsAsync(IEnumerable<DropItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        var copies = new List<DropItem>();
        try
        {
            foreach (var item in items)
            {
                copies.Add(await CopyItemAsync(item));
            }

            return copies;
        }
        catch
        {
            await DeleteOwnedAsync(copies);
            throw;
        }
    }

    public static async Task DeleteOwnedAsync(IEnumerable<DropItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        foreach (var item in items.Where(static item => item.IsOwned && !string.IsNullOrWhiteSpace(item.SourcePath)))
        {
            try
            {
                var file = await StorageFile.GetFileFromPathAsync(item.SourcePath!);
                await file.DeleteAsync(StorageDeleteOption.PermanentDelete);
            }
            catch
            {
                // Missing or externally cleaned materializations are already deleted.
            }
        }
    }

    private static async Task<DropItem> MaterializeImageStreamAsync(
        IRandomAccessStream input,
        string displayName)
    {
        var decoder = await BitmapDecoder.CreateAsync(input);
        var pixels = await decoder.GetPixelDataAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied,
            new BitmapTransform(),
            ExifOrientationMode.RespectExifOrientation,
            ColorManagementMode.ColorManageToSRgb);

        var contentFolder = await GetContentFolderAsync();
        var file = await contentFolder.CreateFileAsync(
            $"drop-{Guid.NewGuid():N}.png",
            CreationCollisionOption.FailIfExists);

        using var output = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output);
        encoder.SetPixelData(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied,
            decoder.OrientedPixelWidth,
            decoder.OrientedPixelHeight,
            decoder.DpiX,
            decoder.DpiY,
            pixels.DetachPixelData());
        await encoder.FlushAsync();

        return DropItem.CreateImage(displayName, file.Path, true);
    }

    private static async Task<DropItem> CopyItemAsync(DropItem item)
    {
        if (item.Kind == DropItemKind.Text)
        {
            return await MaterializeTextAsync(item.Text!);
        }

        if (!item.IsOwned)
        {
            return item.Kind switch
            {
                DropItemKind.Image => DropItem.CreateImage(
                    item.DisplayName,
                    item.SourcePath!),
                DropItemKind.Folder => DropItem.CreateStorageItem(
                    item.DisplayName,
                    item.SourcePath,
                    true),
                _ => DropItem.CreateStorageItem(
                    item.DisplayName,
                    item.SourcePath,
                    false)
            };
        }

        var source = await StorageFile.GetFileFromPathAsync(item.SourcePath!);
        var contentFolder = await GetContentFolderAsync();
        var copy = await source.CopyAsync(
            contentFolder,
            source.Name,
            NameCollisionOption.GenerateUniqueName);
        return item.Kind == DropItemKind.Image
            ? DropItem.CreateImage(item.DisplayName, copy.Path, true)
            : DropItem.CreateStorageItem(
                item.DisplayName,
                copy.Path,
                false,
                true);
    }

    private static Task<StorageFolder> GetContentFolderAsync() =>
        ApplicationData.Current.LocalFolder.CreateFolderAsync(
            ContentFolderName,
            CreationCollisionOption.OpenIfExists).AsTask();
}
