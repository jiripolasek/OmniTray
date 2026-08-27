// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

namespace OmniTray.Core.ThumbnailProviders;

internal sealed class ColorContentThumbnailProvider : IContentThumbnailProvider
{
    public string Id => "omnitray.builtin.thumbnail.color";

    public string DisplayName => "Color swatch thumbnail";

    public int Priority => 100;

    public IReadOnlyList<ContentRequirement> Requirements { get; } =
        [ContentRequirement.All("omnitray.color")];

    public ValueTask<ContentThumbnailDescriptor?> CreateAsync(
        ContentThumbnailContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!ContentDetection.TryNormalizeCssColor(context.Item.Text, out var color))
        {
            return ValueTask.FromResult<ContentThumbnailDescriptor?>(null);
        }

        return ValueTask.FromResult<ContentThumbnailDescriptor?>(
            ContentThumbnailDescriptor.CreateColorSwatch(
                color,
                $"Color {color}",
                color,
                "\uE790"));
    }
}
