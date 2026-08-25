// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

namespace OmniTray.Core;

public static class GameModePolicy
{
    public static bool ShouldSuppressEdgeWindows(
        bool isEnabled,
        bool isRunningD3DFullScreen,
        bool isPresentationMode,
        bool isBusy) =>
        isEnabled && (isRunningD3DFullScreen || isPresentationMode || isBusy);
}
