// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

namespace OmniTray.CommandPalette.Helpers;

internal static class Icons
{
    internal static IconInfo Main { get; } = IconHelpers.FromRelativePath("Assets\\MainIcon.png");
    internal static IconInfo Stack { get; } = new("\uE7B8");
    internal static IconInfo File { get; } = new("\uE8A5");
    internal static IconInfo Folder { get; } = new("\uE838");
    internal static IconInfo Text { get; } = new("\uE8D2");
    internal static IconInfo Image { get; } = new("\uEB9F");
    internal static IconInfo Open { get; } = new("\uE8E5");
    internal static IconInfo Add { get; } = new("\uE710");
    internal static IconInfo Settings { get; } = new("\uE713");
    internal static IconInfo Copy { get; } = new("\uE8C8");
}
