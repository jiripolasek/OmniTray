// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

namespace OmniTray.Core.ThumbnailProviders;

internal sealed class EmailContentThumbnailProvider : IContentThumbnailProvider
{
    public string Id => "omnitray.builtin.thumbnail.email";

    public string DisplayName => "Email thumbnail";

    public int Priority => 105;

    public IReadOnlyList<ContentRequirement> Requirements { get; } =
        [ContentRequirement.All("omnitray.email")];

    public ValueTask<ContentThumbnailDescriptor?> CreateAsync(
        ContentThumbnailContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<ContentThumbnailDescriptor?>(
            ContentThumbnailDescriptor.CreateGlyph("\uE715", "Email address", "email"));
    }
}
