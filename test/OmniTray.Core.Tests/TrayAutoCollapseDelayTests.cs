// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

namespace OmniTray.Core.Tests;

[TestClass]
public sealed class TrayAutoCollapseDelayTests
{
    [TestMethod]
    public void GetDuration_MapsEverySettingChoice()
    {
        var expected = new Dictionary<TrayAutoCollapseDelay, TimeSpan>
        {
            [TrayAutoCollapseDelay.Disabled] = TimeSpan.Zero,
            [TrayAutoCollapseDelay.OneSecond] = TimeSpan.FromSeconds(1),
            [TrayAutoCollapseDelay.TwoSeconds] = TimeSpan.FromSeconds(2),
            [TrayAutoCollapseDelay.ThreeSeconds] = TimeSpan.FromSeconds(3),
            [TrayAutoCollapseDelay.FiveSeconds] = TimeSpan.FromSeconds(5),
            [TrayAutoCollapseDelay.TenSeconds] = TimeSpan.FromSeconds(10),
            [TrayAutoCollapseDelay.TwentySeconds] = TimeSpan.FromSeconds(20),
            [TrayAutoCollapseDelay.ThirtySeconds] = TimeSpan.FromSeconds(30),
            [TrayAutoCollapseDelay.SixtySeconds] = TimeSpan.FromSeconds(60)
        };

        foreach (var (delay, duration) in expected)
        {
            Assert.AreEqual(duration, delay.GetDuration());
        }
    }
}
