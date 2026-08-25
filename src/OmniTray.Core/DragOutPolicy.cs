// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

namespace OmniTray.Core;

public static class DragOutPolicy
{
    public static bool ShouldRequestMove(
        bool moveEnabled,
        bool shiftPressed,
        bool controlPressed,
        IReadOnlyList<DropItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        return moveEnabled &&
               shiftPressed &&
               !controlPressed &&
               items.Count > 0 &&
               items.All(IsOriginalPathBackedItem);
    }

    public static bool ShouldRemoveSource(bool moveRequested, bool moveCompleted) =>
        moveRequested && moveCompleted;

    private static bool IsOriginalPathBackedItem(DropItem item) =>
        !item.IsOwned &&
        !string.IsNullOrWhiteSpace(item.SourcePath) &&
        item.Kind is DropItemKind.File or DropItemKind.Folder or DropItemKind.Image;
}
