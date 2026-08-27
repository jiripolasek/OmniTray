// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

namespace OmniTray.Core.ThumbnailProviders;

internal sealed class XmlContentThumbnailProvider : IContentThumbnailProvider
{
    public string Id => "omnitray.builtin.thumbnail.xml";

    public string DisplayName => "XML thumbnail";

    public int Priority => 106;

    public IReadOnlyList<ContentRequirement> Requirements { get; } =
        [ContentRequirement.All("omnitray.xml")];

    public ValueTask<ContentThumbnailDescriptor?> CreateAsync(
        ContentThumbnailContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<ContentThumbnailDescriptor?>(
            ContentThumbnailDescriptor.CreateGlyph("\uE950", "XML", "xml"));
    }
}
