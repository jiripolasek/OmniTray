// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.ComponentModel;
using System.Diagnostics;
using Microsoft.VisualBasic.FileIO;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.System;

namespace OmniTray.Services;

internal static class ItemManipulationService
{
    public static async Task OpenAsync(DropItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        bool launched;
        if (item.Kind == DropItemKind.Uri &&
            ContentDetection.TryNormalizeWebUrl(item.Url, out var url))
        {
            launched = await Launcher.LaunchUriAsync(new Uri(url));
        }
        else if (Uri.TryCreate(item.ApplicationLink, UriKind.Absolute, out var applicationLink))
        {
            launched = await Launcher.LaunchUriAsync(applicationLink);
        }
        else if (item.Kind == DropItemKind.Folder && !string.IsNullOrWhiteSpace(item.SourcePath))
        {
            launched = await Launcher.LaunchFolderAsync(
                await StorageFolder.GetFolderFromPathAsync(item.SourcePath));
        }
        else if (!string.IsNullOrWhiteSpace(item.SourcePath))
        {
            launched = await Launcher.LaunchFileAsync(
                await StorageFile.GetFileFromPathAsync(item.SourcePath));
        }
        else
        {
            throw new InvalidOperationException("This item does not have content Windows can open.");
        }

        if (!launched)
        {
            throw new InvalidOperationException("Windows could not open this item.");
        }
    }

    public static async Task OpenSourceUrlAsync(DropItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!ContentDetection.TryNormalizeWebUrl(item.SourceUrl, out var sourceUrl) ||
            !await Launcher.LaunchUriAsync(new Uri(sourceUrl)))
        {
            throw new InvalidOperationException("Windows could not open the saved source URL.");
        }
    }

    public static async Task OpenContainingFolderAsync(DropItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentException.ThrowIfNullOrWhiteSpace(item.SourcePath);

        var sourcePath = Path.TrimEndingDirectorySeparator(item.SourcePath);
        var parentPath = Path.GetDirectoryName(sourcePath);
        if (string.IsNullOrWhiteSpace(parentPath))
        {
            if (item.Kind == DropItemKind.Folder &&
                !await Launcher.LaunchFolderPathAsync(sourcePath))
            {
                throw new InvalidOperationException("Windows could not open this folder.");
            }

            return;
        }

        var source = item.Kind == DropItemKind.Folder
            ? (IStorageItem)await StorageFolder.GetFolderFromPathAsync(sourcePath)
            : await StorageFile.GetFileFromPathAsync(sourcePath);
        var parent = await StorageFolder.GetFolderFromPathAsync(parentPath);
        var options = new FolderLauncherOptions();
        options.ItemsToSelect.Add(source);
        if (!await Launcher.LaunchFolderAsync(parent, options))
        {
            throw new InvalidOperationException("Windows could not show this item in File Explorer.");
        }
    }

    public static void PutOnClipboard(
        IReadOnlyList<DropItem> items,
        DataPackageOperation operation)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
        {
            throw new ArgumentException("At least one item is required.", nameof(items));
        }

        if (operation == DataPackageOperation.Move &&
            items.Any(static item => !ContentMetadataPolicy.HasAction(item, ContentActions.Cut)))
        {
            throw new InvalidOperationException("Only original files and folders can be cut.");
        }

        var data = new DataPackage();
        DragDropDataService.WriteStandardContent(
            data,
            items,
            items.Count == 1 ? items[0].DisplayName : $"{items.Count} OmniTray items",
            operation);
        Clipboard.SetContent(data);
        Clipboard.Flush();
    }

    public static void ShowProperties(DropItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentException.ThrowIfNullOrWhiteSpace(item.SourcePath);

        try
        {
            var process = Process.Start(new ProcessStartInfo(item.SourcePath)
            {
                UseShellExecute = true,
                Verb = "properties"
            });
            process?.Dispose();
        }
        catch (Win32Exception exception)
        {
            throw new InvalidOperationException("Windows could not show properties for this item.", exception);
        }
    }

    public static Task<ItemFileSystemDeleteResult> RecycleAsync(IReadOnlyList<DropItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return Task.Run(() =>
        {
            var deletedIds = new List<Guid>();
            var errors = new List<string>();
            foreach (var item in items)
            {
                try
                {
                    if (!ContentMetadataPolicy.HasAction(item, ContentActions.Delete))
                    {
                        throw new InvalidOperationException("This is not an original filesystem item.");
                    }

                    if (item.Kind == DropItemKind.Folder)
                    {
                        var path = Path.TrimEndingDirectorySeparator(item.SourcePath!);
                        if (string.Equals(path, Path.GetPathRoot(path), StringComparison.OrdinalIgnoreCase))
                        {
                            throw new InvalidOperationException("Filesystem roots cannot be deleted.");
                        }

                        FileSystem.DeleteDirectory(
                            path,
                            UIOption.OnlyErrorDialogs,
                            RecycleOption.SendToRecycleBin,
                            UICancelOption.ThrowException);
                    }
                    else
                    {
                        FileSystem.DeleteFile(
                            item.SourcePath!,
                            UIOption.OnlyErrorDialogs,
                            RecycleOption.SendToRecycleBin,
                            UICancelOption.ThrowException);
                    }

                    deletedIds.Add(item.Id);
                }
                catch (Exception exception)
                {
                    errors.Add($"{item.DisplayName}: {exception.Message}");
                }
            }

            return new ItemFileSystemDeleteResult(
                deletedIds,
                items.Count - deletedIds.Count,
                errors.Count == 0 ? null : string.Join(Environment.NewLine, errors));
        });
    }
}

internal sealed record ItemFileSystemDeleteResult(
    IReadOnlyList<Guid> DeletedItemIds,
    int FailedCount,
    string? ErrorMessage);
