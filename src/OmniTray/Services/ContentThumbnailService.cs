// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

using System.Runtime.InteropServices;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Streams;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace OmniTray.Services;

internal sealed record ContentThumbnailPresentation(
    ImageSource? ImageSource,
    ContentThumbnailChrome Chrome,
    string Glyph,
    string AccessibleLabel,
    string ProviderId);

internal sealed record ShellThumbnailPresentation(
    ImageSource ImageSource,
    ContentThumbnailChrome Chrome);

internal sealed class ContentThumbnailService
{
    private const uint DefaultThumbnailSize = 120;
    private const uint ContentThumbnailSize = 256;
    private const uint ShellIconThumbnailSize = 128;
    private const uint VideoThumbnailWidth = 190;
    private const int PendingThumbnailHResult = unchecked((int)0x8000000A);
    private const int ShellThumbnailAttemptCount = 3;
    private static readonly SemaphoreSlim ShellThumbnailGate = new(4, 4);
    private readonly ContentThumbnailRegistry _registry;

    public static ContentThumbnailService Default { get; } =
        new(ContentThumbnailRegistry.Default);

    public ContentThumbnailService(ContentThumbnailRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        this._registry = registry;
    }

    public async Task<ContentThumbnailPresentation> ResolveAsync(
        DropItem item,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        var metadata = ContentMetadataPolicy.GetMetadata(item);
        var resolution = await this._registry.ResolveAsync(
            new ContentThumbnailContext(
                item,
                metadata,
                new ContentThumbnailRequest { PixelSize = DefaultThumbnailSize }),
            cancellationToken);
        var descriptor = resolution.Thumbnail;
        if (descriptor is not null && !descriptor.IsFallback)
        {
            try
            {
                var providerImage = await CreateImageSourceAsync(descriptor, cancellationToken);
                if (providerImage is not null || descriptor.Kind == ContentThumbnailKind.Glyph)
                {
                    return CreatePresentation(descriptor, providerImage);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // A provider rendering failure falls through to the shell and stable glyph paths.
            }
        }

        var shellThumbnail = await TryLoadShellThumbnailAsync(item, metadata, cancellationToken);
        if (shellThumbnail is not null)
        {
            return new ContentThumbnailPresentation(
                shellThumbnail.ImageSource,
                shellThumbnail.Chrome,
                descriptor?.Glyph ?? "\uE7B8",
                $"{item.DisplayName} thumbnail",
                "omnitray.shell-thumbnail");
        }

        return descriptor is null
            ? new ContentThumbnailPresentation(
                null,
                ContentThumbnailChrome.Default,
                "\uE7B8",
                "Content",
                string.Empty)
            : CreatePresentation(descriptor, null);
    }

    private static ContentThumbnailPresentation CreatePresentation(
        ContentThumbnailDescriptor descriptor,
        ImageSource? imageSource) =>
        new(
            imageSource,
            descriptor.Chrome,
            descriptor.Glyph ?? "\uE7B8",
            descriptor.AccessibleLabel,
            descriptor.ProviderId);

    private static async Task<ImageSource?> CreateImageSourceAsync(
        ContentThumbnailDescriptor descriptor,
        CancellationToken cancellationToken)
    {
        switch (descriptor.Kind)
        {
            case ContentThumbnailKind.ColorSwatch:
                return await CreateColorSwatchAsync(descriptor.Color!, cancellationToken);
            case ContentThumbnailKind.EncodedImage:
                return await CreateBitmapAsync(descriptor.EncodedData!, cancellationToken);
            default:
                return null;
        }
    }

    private static async Task<ImageSource> CreateColorSwatchAsync(
        string color,
        CancellationToken cancellationToken)
    {
        var svg
            = $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"120\" height=\"120\" viewBox=\"0 0 120 120\"><rect width=\"120\" height=\"120\" fill=\"{color}\"/></svg>";
        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream))
        {
            writer.UnicodeEncoding = UnicodeEncoding.Utf8;
            writer.WriteString(svg);
            _ = await writer.StoreAsync();
            await writer.FlushAsync();
            _ = writer.DetachStream();
        }

        cancellationToken.ThrowIfCancellationRequested();
        stream.Seek(0);
        var source = new SvgImageSource();
        await source.SetSourceAsync(stream);
        return source;
    }

    private static async Task<ImageSource> CreateBitmapAsync(
        byte[] encodedData,
        CancellationToken cancellationToken)
    {
        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream))
        {
            writer.WriteBytes(encodedData);
            _ = await writer.StoreAsync();
            await writer.FlushAsync();
            _ = writer.DetachStream();
        }

        cancellationToken.ThrowIfCancellationRequested();
        stream.Seek(0);
        var source = new BitmapImage();
        await source.SetSourceAsync(stream);
        return source;
    }

    private static async Task<ShellThumbnailPresentation?> TryLoadShellThumbnailAsync(
        DropItem item,
        ContentMetadata metadata,
        CancellationToken cancellationToken)
    {
        await ShellThumbnailGate.WaitAsync(cancellationToken);
        try
        {
            for (var attempt = 0; attempt < ShellThumbnailAttemptCount; attempt++)
            {
                try
                {
                    return await TryLoadShellThumbnailOnceAsync(item, metadata, cancellationToken);
                }
                catch (COMException exception)
                    when (exception.HResult == PendingThumbnailHResult &&
                          attempt < ShellThumbnailAttemptCount - 1)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(50 * (attempt + 1)), cancellationToken);
                }
                catch (COMException exception)
                    when (exception.HResult == PendingThumbnailHResult)
                {
                    return null;
                }
            }

            return null;
        }
        finally
        {
            ShellThumbnailGate.Release();
        }
    }

    private static async Task<ShellThumbnailPresentation?> TryLoadShellThumbnailOnceAsync(
        DropItem item,
        ContentMetadata metadata,
        CancellationToken cancellationToken)
    {
        if (!metadata.HasLocalPath || string.IsNullOrWhiteSpace(item.SourcePath))
        {
            return null;
        }

        StorageItemThumbnail? thumbnail = null;
        var hasIntrinsicVisualContent = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (metadata.HasFolder)
            {
                var folder = await StorageFolder.GetFolderFromPathAsync(item.SourcePath);
                thumbnail = await folder.GetThumbnailAsync(
                    ThumbnailMode.SingleItem,
                    ContentThumbnailSize,
                    ThumbnailOptions.UseCurrentScale);
            }
            else
            {
                var file = await StorageFile.GetFileFromPathAsync(item.SourcePath);
                var isVideo = ContentDetection.IsVideoFile(file.ContentType, file.FileType);
                hasIntrinsicVisualContent =
                    isVideo || file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
                var thumbnailMode = isVideo
                    ? ThumbnailMode.VideosView
                    : ThumbnailMode.SingleItem;
                var thumbnailSize = isVideo
                    ? VideoThumbnailWidth
                    : hasIntrinsicVisualContent
                        ? ContentThumbnailSize
                        : ShellIconThumbnailSize;
                thumbnail = await file.GetThumbnailAsync(
                    thumbnailMode,
                    thumbnailSize,
                    ThumbnailOptions.UseCurrentScale);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (thumbnail is null)
            {
                return null;
            }

            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(thumbnail);
            var isShellIcon = ContentDetection.IsLikelyShellIconThumbnail(
                thumbnail.Type == ThumbnailType.Icon,
                hasIntrinsicVisualContent,
                thumbnail.OriginalWidth,
                thumbnail.OriginalHeight);
            return new ShellThumbnailPresentation(
                bitmap,
                isShellIcon
                    ? ContentThumbnailChrome.None
                    : ContentThumbnailChrome.Default);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (COMException exception)
            when (exception.HResult == PendingThumbnailHResult)
        {
            throw;
        }
        catch
        {
            // Missing, inaccessible, and unsupported sources retain the provider fallback.
            return null;
        }
        finally
        {
            thumbnail?.Dispose();
        }
    }
}
