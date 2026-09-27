using System.Diagnostics;
using MouseGesture.Core.Input;

namespace MouseGesture.Core.Tests;

[TestClass]
public sealed class PreciseDelayTests
{
    [TestMethod]
    public void Wait1ms_IsNotRoundedUpToTheSystemTimerPeriod()
    {
        using var delay = new PreciseDelay();
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < 20; i++)
            delay.Wait(TimeSpan.FromMilliseconds(1));
        sw.Stop();

        // Thread.Sleep(1) x20 takes ~300 ms at the default 15.6 ms timer period.
        Assert.IsTrue(sw.ElapsedMilliseconds < 60, $"20 x 1 ms took {sw.ElapsedMilliseconds} ms");
        Assert.IsTrue(sw.ElapsedMilliseconds >= 15, $"20 x 1 ms took only {sw.ElapsedMilliseconds} ms");
    }
}
