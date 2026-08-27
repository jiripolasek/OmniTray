// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

namespace OmniTray.Core.ThumbnailProviders;

internal sealed class PrimaryKindContentThumbnailProvider : IContentThumbnailProvider
{
    public string Id => "omnitray.builtin.thumbnail.primary-kind";

    public string DisplayName => "Primary content type thumbnail";

    public int Priority => 1000;

    public IReadOnlyList<ContentRequirement> Requirements { get; } = [];

    public ValueTask<ContentThumbnailDescriptor?> CreateAsync(
        ContentThumbnailContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<ContentThumbnailDescriptor?>(
            ContentThumbnailFallback.For(context.Item.Kind));
    }
}
