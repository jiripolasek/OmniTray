// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

namespace OmniTray.Core.ThumbnailProviders;

internal sealed class TableContentThumbnailProvider : IContentThumbnailProvider
{
    public string Id => "omnitray.builtin.thumbnail.table";

    public string DisplayName => "Table thumbnail";

    public int Priority => 110;

    public IReadOnlyList<ContentRequirement> Requirements { get; } =
        [ContentRequirement.All("omnitray.table")];

    public ValueTask<ContentThumbnailDescriptor?> CreateAsync(
        ContentThumbnailContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<ContentThumbnailDescriptor?>(
            ContentThumbnailDescriptor.CreateGlyph("\uE80A", "Tabular content", "table"));
    }
}
