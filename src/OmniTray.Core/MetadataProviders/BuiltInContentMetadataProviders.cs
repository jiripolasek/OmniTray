// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

namespace OmniTray.Core.MetadataProviders;

internal static class BuiltInContentMetadataProviders
{
    public static IReadOnlyList<IContentMetadataProvider> Create() =>
    [
        new StandardContentMetadataProvider()
    ];
}
