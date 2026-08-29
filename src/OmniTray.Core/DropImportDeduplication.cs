// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

namespace OmniTray.Core;

public static class DropImportDeduplication
{
    public static IReadOnlyList<DropItem> FilterNewItems(
        IEnumerable<DropItem> existingItems,
        IEnumerable<DropItem> candidateItems)
    {
        ArgumentNullException.ThrowIfNull(existingItems);
        ArgumentNullException.ThrowIfNull(candidateItems);

        var existing = existingItems.ToArray();
        var seenIds = existing.Select(static item => item.Id).ToHashSet();
        var seenOriginalPaths = existing
            .Select(GetOriginalFileSystemPathKey)
            .Where(static path => path is not null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var additions = new List<DropItem>();

        foreach (var candidate in candidateItems)
        {
            if (!seenIds.Add(candidate.Id))
            {
                continue;
            }

            var originalPath = GetOriginalFileSystemPathKey(candidate);
            if (originalPath is not null && !seenOriginalPaths.Add(originalPath))
            {
                continue;
            }

            additions.Add(candidate);
        }

        return additions;
    }

    private static string? GetOriginalFileSystemPathKey(DropItem item)
    {
        if (item.IsOwned ||
            item.Kind == DropItemKind.Text ||
            string.IsNullOrWhiteSpace(item.SourcePath))
        {
            return null;
        }

        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(item.SourcePath));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException
                                              or PathTooLongException)
        {
            return Path.TrimEndingDirectorySeparator(item.SourcePath.Trim());
        }
    }
}
