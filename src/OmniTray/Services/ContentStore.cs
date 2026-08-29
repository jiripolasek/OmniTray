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
    private const int MaxHtmlResourceCount = 16;
    private const ulong MaxHtmlResourceBytes = 4UL * 1024 * 1024;
    private const ulong MaxHtmlResourceTotalBytes = 16UL * 1024 * 1024;

    public static Task<DropItem> MaterializeTextAsync(string text) =>
        MaterializeTextAsync(text, null, null, null, null);

    public static async Task<DropItem> MaterializeTextAsync(
        string? text,
        string? html,
        string? rtf,
        string? sourceUrl,
        string? sourceApplicationName)
    {
        if (string.IsNullOrWhiteSpace(text) &&
            string.IsNullOrWhiteSpace(html) &&
            string.IsNullOrWhiteSpace(rtf))
        {
            throw new ArgumentException("At least one text representation is required.", nameof(text));
        }

        var contentFolder = await GetContentFolderAsync();
        var file = await contentFolder.CreateFileAsync(
            $"text-{Guid.NewGuid():N}.txt",
            CreationCollisionOption.FailIfExists);
        var materializedText = !string.IsNullOrWhiteSpace(text)
            ? text
            : !string.IsNullOrWhiteSpace(html)
                ? ContentDetection.ExtractPlainTextFromHtml(html)
                : "Rich text content";
        await FileIO.WriteTextAsync(file, materializedText);
        return DropItem.CreateRichText(
            text,
            html,
            rtf,
            file.Path,
            true,
            sourceUrl,
            sourceApplicationName);
    }

    public static async Task<DropItem> MaterializeBitmapAsync(
        RandomAccessStreamReference bitmapReference,
        string displayName = "Dropped image",
        string? text = null,
        string? html = null,
        string? rtf = null,
        string? sourceUrl = null,
        string? sourceApplicationName = null)
    {
        ArgumentNullException.ThrowIfNull(bitmapReference);

        using var input = await bitmapReference.OpenReadAsync();
        return await MaterializeImageStreamAsync(
            input,
            displayName,
            text,
            html,
            rtf,
            sourceUrl,
            sourceApplicationName);
    }

    public static async Task<DropItem> MaterializeImageFileAsync(
        StorageFile source,
        string? text = null,
        string? html = null,
        string? rtf = null,
        string? sourceUrl = null,
        string? sourceApplicationName = null)
    {
        ArgumentNullException.ThrowIfNull(source);

        using var input = await source.OpenReadAsync();
        var displayName = string.IsNullOrWhiteSpace(source.DisplayName)
            ? "Dropped image"
            : source.DisplayName;
        return await MaterializeImageStreamAsync(
            input,
            displayName,
            text,
            html,
            rtf,
            sourceUrl,
            sourceApplicationName);
    }

    public static async Task<DropItem> MaterializeVirtualFileAsync(
        StorageFile source,
        string? text = null,
        string? html = null,
        string? rtf = null,
        string? sourceUrl = null,
        string? sourceApplicationName = null)
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

        return DropItem.CreateStorageItem(copy.Name, copy.Path, false, true).WithRepresentations(
            text,
            html,
            rtf,
            sourceUrl,
            sourceApplicationName);
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

    public static async Task DeleteOwnedAsync(IEnumerable<DropItem> items, IEnumerable<DropItem>? retainedItems = null)
    {
        ArgumentNullException.ThrowIfNull(items);

        var retained = retainedItems?.ToArray() ?? [];
        var retainedPaths = retained.Select(item => item.SourcePath).OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var retainedResources = retained.SelectMany(item => item.HtmlResources)
            .Select(resource => resource.ManagedRelativePath).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var item in items.Where(item => item.IsOwned && !string.IsNullOrWhiteSpace(item.SourcePath)
                                                              && !retainedPaths.Contains(item.SourcePath)))
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

        await DeleteHtmlResourcesAsync(items.SelectMany(static item => item.HtmlResources)
            .Where(resource => !retainedResources.Contains(resource.ManagedRelativePath)));
    }

    public static async Task DeleteHtmlResourcesAsync(IEnumerable<DropItemHtmlResource> resources)
    {
        ArgumentNullException.ThrowIfNull(resources);

        foreach (var resource in resources)
        {
            try
            {
                var file = await ApplicationData.Current.LocalFolder.GetFileAsync(resource.ManagedRelativePath);
                await file.DeleteAsync(StorageDeleteOption.PermanentDelete);
            }
            catch
            {
                // Missing or externally cleaned HTML resources are already deleted.
            }
        }
    }

    public static async Task<IReadOnlyList<DropItemHtmlResource>> MaterializeHtmlResourcesAsync(
        IReadOnlyDictionary<string, RandomAccessStreamReference> resourceMap)
    {
        ArgumentNullException.ThrowIfNull(resourceMap);

        var resources = new List<DropItemHtmlResource>();
        ulong totalBytes = 0;
        foreach (var pair in resourceMap)
        {
            if (resources.Count >= MaxHtmlResourceCount)
            {
                break;
            }

            try
            {
                using var input = await pair.Value.OpenReadAsync();
                if (input.Size > MaxHtmlResourceBytes ||
                    input.Size > MaxHtmlResourceTotalBytes - totalBytes)
                {
                    continue;
                }

                var contentFolder = await GetContentFolderAsync();
                var extension = GetSafeResourceExtension(pair.Key);
                var file = await contentFolder.CreateFileAsync(
                    $"html-resource-{Guid.NewGuid():N}{extension}",
                    CreationCollisionOption.FailIfExists);
                try
                {
                    using var output = await file.OpenAsync(FileAccessMode.ReadWrite);
                    await RandomAccessStream.CopyAsync(
                        input.GetInputStreamAt(0),
                        output.GetOutputStreamAt(0));
                    await output.FlushAsync();
                }
                catch
                {
                    await file.DeleteAsync(StorageDeleteOption.PermanentDelete);
                    throw;
                }

                resources.Add(new DropItemHtmlResource
                {
                    ResourceKey = pair.Key,
                    ManagedRelativePath = $"{ContentFolderName}\\{file.Name}",
                    Size = input.Size
                });
                totalBytes += input.Size;
            }
            catch
            {
                // A missing or unsupported resource must not prevent capture of the HTML itself.
            }
        }

        return resources;
    }

    public static Uri CreateHtmlResourceUri(DropItemHtmlResource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        var segments = resource.ManagedRelativePath
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.EscapeDataString);
        return new Uri($"ms-appdata:///local/{string.Join("/", segments)}");
    }

    private static async Task<DropItem> MaterializeImageStreamAsync(
        IRandomAccessStream input,
        string displayName,
        string? text,
        string? html,
        string? rtf,
        string? sourceUrl,
        string? sourceApplicationName)
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

        return DropItem.CreateImage(
            displayName,
            file.Path,
            true,
            text,
            html,
            rtf,
            sourceUrl,
            sourceApplicationName);
    }

    private static async Task<DropItem> CopyItemAsync(DropItem item)
    {
        if (item.Note is { } note)
        {
            return DropItem.CreateNote(note.Duplicate());
        }

        DropItem copy;
        if (item.Kind == DropItemKind.Text)
        {
            copy = await MaterializeTextAsync(
                item.Text,
                item.Html,
                item.Rtf,
                item.SourceUrl,
                item.SourceApplicationName);
            return await CompleteCopyAsync(item, copy);
        }

        if (item.Kind == DropItemKind.Uri)
        {
            copy = DropItem.CreateUri(
                item.Url!,
                item.DisplayName,
                item.Text,
                item.Html,
                item.Rtf,
                item.SourceUrl,
                item.SourceApplicationName,
                item.ApplicationLink);
            return await CompleteCopyAsync(item, copy);
        }

        if (!item.IsOwned)
        {
            copy = item.Kind switch
            {
                DropItemKind.Image => DropItem.CreateImage(
                    item.DisplayName,
                    item.SourcePath!,
                    false,
                    item.Text,
                    item.Html,
                    item.Rtf,
                    item.SourceUrl,
                    item.SourceApplicationName,
                    item.ApplicationLink),
                DropItemKind.Folder => DropItem.CreateStorageItem(
                    item.DisplayName,
                    item.SourcePath,
                    true).WithRepresentations(
                    item.Text,
                    item.Html,
                    item.Rtf,
                    item.SourceUrl,
                    item.SourceApplicationName,
                    item.ApplicationLink),
                _ => DropItem.CreateStorageItem(
                    item.DisplayName,
                    item.SourcePath,
                    false).WithRepresentations(
                    item.Text,
                    item.Html,
                    item.Rtf,
                    item.SourceUrl,
                    item.SourceApplicationName,
                    item.ApplicationLink)
            };
            return await CompleteCopyAsync(item, copy);
        }

        var source = await StorageFile.GetFileFromPathAsync(item.SourcePath!);
        var contentFolder = await GetContentFolderAsync();
        var storageCopy = await source.CopyAsync(
            contentFolder,
            source.Name,
            NameCollisionOption.GenerateUniqueName);
        copy = item.Kind == DropItemKind.Image
            ? DropItem.CreateImage(
                item.DisplayName,
                storageCopy.Path,
                true,
                item.Text,
                item.Html,
                item.Rtf,
                item.SourceUrl,
                item.SourceApplicationName,
                item.ApplicationLink)
            : DropItem.CreateStorageItem(
                item.DisplayName,
                storageCopy.Path,
                false,
                true).WithRepresentations(
                item.Text,
                item.Html,
                item.Rtf,
                item.SourceUrl,
                item.SourceApplicationName,
                item.ApplicationLink);
        return await CompleteCopyAsync(item, copy.WithMetadata(
            backing: new ContentBacking { Kind = ContentBackingKind.ManagedSnapshot, Path = copy.SourcePath }));
    }

    private static async Task<DropItem> CompleteCopyAsync(DropItem source, DropItem copy)
    {
        var copiedResources = new List<DropItemHtmlResource>(source.HtmlResources.Count);
        try
        {
            var contentFolder = await GetContentFolderAsync();
            foreach (var resource in source.HtmlResources)
            {
                var original = await ApplicationData.Current.LocalFolder.GetFileAsync(resource.ManagedRelativePath);
                var cloned = await original.CopyAsync(
                    contentFolder,
                    original.Name,
                    NameCollisionOption.GenerateUniqueName);
                copiedResources.Add(resource with { ManagedRelativePath = $"{ContentFolderName}\\{cloned.Name}" });
            }

            return copy
                .WithAttachedNotes(source.AttachedNotes.Select(static note => note.Duplicate()))
                .WithCustomFormats(source.CustomFormats)
                .WithMetadata(
                    source.Provenance,
                    source.Capture,
                    copy.Backing,
                    source.FileFacts,
                    copiedResources);
        }
        catch
        {
            await DeleteOwnedAsync([copy.WithMetadata(htmlResources: copiedResources)]);
            throw;
        }
    }

    private static string GetSafeResourceExtension(string resourceKey)
    {
        var extension = Path.GetExtension(resourceKey);
        return extension.Length is > 1 and <= 12 &&
               extension.Skip(1).All(static character => char.IsAsciiLetterOrDigit(character))
            ? extension.ToLowerInvariant()
            : ".bin";
    }

    private static Task<StorageFolder> GetContentFolderAsync() =>
        ApplicationData.Current.LocalFolder.CreateFolderAsync(
            ContentFolderName,
            CreationCollisionOption.OpenIfExists).AsTask();
}
