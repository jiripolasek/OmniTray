// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

namespace OmniTray.Core.ThumbnailProviders;

internal static class BuiltInContentThumbnailProviders
{
    public static IReadOnlyList<IContentThumbnailProvider> Create() =>
    [
        new ColorContentThumbnailProvider(),
        new EmailContentThumbnailProvider(),
        new XmlContentThumbnailProvider(),
        new MarkdownContentThumbnailProvider(),
        new TableContentThumbnailProvider(),
        new CodeContentThumbnailProvider(),
        new PrimaryKindContentThumbnailProvider()
    ];
}
