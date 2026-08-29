// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

namespace OmniTray.Core.ThumbnailProviders;

internal sealed class CodeContentThumbnailProvider : IContentThumbnailProvider
{
    public string Id => "omnitray.builtin.thumbnail.code";

    public string DisplayName => "Code thumbnail";

    public int Priority => 115;

    public IReadOnlyList<ContentRequirement> Requirements { get; } =
        [ContentRequirement.All("omnitray.code")];

    public ValueTask<ContentThumbnailDescriptor?> CreateAsync(
        ContentThumbnailContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<ContentThumbnailDescriptor?>(
            ContentThumbnailDescriptor.CreateGlyph("\uE943", "Code", "code"));
    }
}
