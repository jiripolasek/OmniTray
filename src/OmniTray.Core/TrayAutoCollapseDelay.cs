// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

namespace OmniTray.Core;

public enum TrayAutoCollapseDelay
{
    Disabled = 0,
    FiveSeconds = 1,
    TenSeconds = 2,
    TwentySeconds = 3,
    ThirtySeconds = 4,
    SixtySeconds = 5,
    OneSecond = 6,
    TwoSeconds = 7,
    ThreeSeconds = 8
}

public static class TrayAutoCollapseDelayExtensions
{
    public static TimeSpan GetDuration(this TrayAutoCollapseDelay delay) =>
        delay switch
        {
            TrayAutoCollapseDelay.Disabled => TimeSpan.Zero,
            TrayAutoCollapseDelay.OneSecond => TimeSpan.FromSeconds(1),
            TrayAutoCollapseDelay.TwoSeconds => TimeSpan.FromSeconds(2),
            TrayAutoCollapseDelay.ThreeSeconds => TimeSpan.FromSeconds(3),
            TrayAutoCollapseDelay.FiveSeconds => TimeSpan.FromSeconds(5),
            TrayAutoCollapseDelay.TenSeconds => TimeSpan.FromSeconds(10),
            TrayAutoCollapseDelay.TwentySeconds => TimeSpan.FromSeconds(20),
            TrayAutoCollapseDelay.ThirtySeconds => TimeSpan.FromSeconds(30),
            TrayAutoCollapseDelay.SixtySeconds => TimeSpan.FromSeconds(60),
            _ => throw new ArgumentOutOfRangeException(nameof(delay))
        };
}
