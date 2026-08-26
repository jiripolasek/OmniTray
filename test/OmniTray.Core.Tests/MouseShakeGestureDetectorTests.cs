// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

namespace OmniTray.Core.Tests;

[TestClass]
public sealed class MouseShakeGestureDetectorTests
{
    private static readonly DateTimeOffset Start = new(2026, 8, 26, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void Update_TriggersAfterThreeQuickHorizontalReversals()
    {
        var detector = new MouseShakeGestureDetector(30, 3, TimeSpan.FromMilliseconds(700));

        Assert.IsFalse(detector.Update(100, 100, Start));
        Assert.IsFalse(detector.Update(140, 100, Start.AddMilliseconds(100)));
        Assert.IsFalse(detector.Update(100, 101, Start.AddMilliseconds(200)));
        Assert.IsFalse(detector.Update(140, 99, Start.AddMilliseconds(300)));
        Assert.IsTrue(detector.Update(100, 100, Start.AddMilliseconds(400)));
    }

    [TestMethod]
    public void Update_TriggersForVerticalShake()
    {
        var detector = new MouseShakeGestureDetector(30, 3, TimeSpan.FromMilliseconds(700));

        Assert.IsFalse(detector.Update(100, 100, Start));
        Assert.IsFalse(detector.Update(100, 140, Start.AddMilliseconds(100)));
        Assert.IsFalse(detector.Update(101, 100, Start.AddMilliseconds(200)));
        Assert.IsFalse(detector.Update(99, 140, Start.AddMilliseconds(300)));
        Assert.IsTrue(detector.Update(100, 100, Start.AddMilliseconds(400)));
    }

    [TestMethod]
    public void Update_DoesNotTriggerForSteadyMovementOrJitter()
    {
        var detector = new MouseShakeGestureDetector(30, 3, TimeSpan.FromMilliseconds(700));

        Assert.IsFalse(detector.Update(100, 100, Start));
        Assert.IsFalse(detector.Update(140, 105, Start.AddMilliseconds(100)));
        Assert.IsFalse(detector.Update(180, 95, Start.AddMilliseconds(200)));
        Assert.IsFalse(detector.Update(220, 104, Start.AddMilliseconds(300)));
        Assert.IsFalse(detector.Update(260, 96, Start.AddMilliseconds(400)));
    }

    [TestMethod]
    public void Update_DoesNotCombineStrokesOutsideTheTimeWindow()
    {
        var detector = new MouseShakeGestureDetector(30, 3, TimeSpan.FromMilliseconds(500));

        Assert.IsFalse(detector.Update(100, 100, Start));
        Assert.IsFalse(detector.Update(140, 100, Start.AddMilliseconds(100)));
        Assert.IsFalse(detector.Update(100, 100, Start.AddMilliseconds(200)));
        Assert.IsFalse(detector.Update(140, 100, Start.AddMilliseconds(700)));
        Assert.IsFalse(detector.Update(100, 100, Start.AddMilliseconds(800)));
    }

    [TestMethod]
    public void Update_TriggersOnlyOnceUntilReset()
    {
        var detector = new MouseShakeGestureDetector(30, 3, TimeSpan.FromMilliseconds(700));

        Assert.IsFalse(detector.Update(100, 100, Start));
        Assert.IsFalse(detector.Update(140, 100, Start.AddMilliseconds(100)));
        Assert.IsFalse(detector.Update(100, 100, Start.AddMilliseconds(200)));
        Assert.IsFalse(detector.Update(140, 100, Start.AddMilliseconds(300)));
        Assert.IsTrue(detector.Update(100, 100, Start.AddMilliseconds(400)));
        Assert.IsFalse(detector.Update(140, 100, Start.AddMilliseconds(500)));

        detector.Reset();

        Assert.IsFalse(detector.Update(100, 100, Start.AddSeconds(1)));
        Assert.IsFalse(detector.Update(140, 100, Start.AddMilliseconds(1100)));
        Assert.IsFalse(detector.Update(100, 100, Start.AddMilliseconds(1200)));
        Assert.IsFalse(detector.Update(140, 100, Start.AddMilliseconds(1300)));
        Assert.IsTrue(detector.Update(100, 100, Start.AddMilliseconds(1400)));
    }
}
