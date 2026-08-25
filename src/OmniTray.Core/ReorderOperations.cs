// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

namespace OmniTray.Core;

public static class ReorderOperations
{
    public static int ResolveDestinationIndex(
        int sourceIndex,
        int insertionIndex,
        int itemCount)
    {
        if (sourceIndex < 0 || sourceIndex >= itemCount)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceIndex));
        }

        if (insertionIndex < 0 || insertionIndex > itemCount)
        {
            throw new ArgumentOutOfRangeException(nameof(insertionIndex));
        }

        return insertionIndex > sourceIndex
            ? insertionIndex - 1
            : insertionIndex;
    }

    public static bool WouldMove(
        int sourceIndex,
        int insertionIndex,
        int itemCount) =>
        ResolveDestinationIndex(sourceIndex, insertionIndex, itemCount) != sourceIndex;
}
