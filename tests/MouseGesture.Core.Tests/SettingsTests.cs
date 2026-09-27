using MouseGesture.Core.Persistence;
using MouseGesture.Core.Recognition;

namespace MouseGesture.Core.Tests;

[TestClass]
public sealed class AppSettingsTests
{
    [TestMethod]
    public void WheelEffective_FalseWhenModifierEqualsTrigger()
    {
        var s = AppSettings.Default with
        {
            Trigger = TriggerButton.XButton1,
            Wheel = new WheelSettings(true, TriggerButton.XButton1, 3),
        };
        Assert.IsFalse(s.IsWheelEffective);
        Assert.IsTrue((s with { Trigger = TriggerButton.Right }).IsWheelEffective);
        Assert.IsFalse((s with { Trigger = TriggerButton.Right, Wheel = s.Wheel with { Enabled = false } }).IsWheelEffective);
    }

    [TestMethod]
    public void OtherSideButton_Swaps()
    {
        Assert.AreEqual(TriggerButton.XButton2, TriggerButton.XButton1.OtherSideButton());
        Assert.AreEqual(TriggerButton.XButton1, TriggerButton.XButton2.OtherSideButton());
    }
}

[TestClass]
public sealed class ScreenProbeTests
{
    [TestMethod]
    public void GetRunningApps_OnePerExecutable_ExcludesSelf()
    {
        var apps = ScreenProbe.GetRunningApps();
        var self = Path.GetFileName(Environment.ProcessPath)!;

        Assert.AreEqual(apps.Count, apps.Select(a => a.ProcessName).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.IsFalse(apps.Any(a => string.Equals(a.ProcessName, self, StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(apps.All(a => a.WindowTitle.Length > 0));
    }

    [TestMethod]
    [DataRow(@" C:\Games\Game.EXE ", "game.exe")]
    [DataRow("notepad", "notepad.exe")]
    [DataRow("", "")]
    public void NormalizeProcessName(string raw, string expected)
        => Assert.AreEqual(expected, ScreenProbe.NormalizeProcessName(raw));
}

[TestClass]
public sealed class StrokeFormatTests
{
    [TestMethod]
    [DataRow("u", "U")]
    [DataRow(" urdl ", "URDL")]
    [DataRow("LRLRLRLR", "LRLRLRLR")]
    public void Valid_Normalized(string raw, string expected)
    {
        Assert.IsTrue(StrokeFormat.TryNormalize(raw, out var s));
        Assert.AreEqual(expected, s);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow(null)]
    [DataRow("UU")]
    [DataRow("UX")]
    [DataRow("LRLRLRLRL")]
    public void Invalid_Rejected(string? raw)
    {
        Assert.IsFalse(StrokeFormat.TryNormalize(raw, out _));
    }
}

[TestClass]
public sealed class DirectionExtensionTests
{
    [TestMethod]
    public void StrokeToArrows_MapsAllFourDirections()
    {
        Assert.AreEqual("↑→↓←", DirectionExtensions.StrokeToArrows("URDL"));
    }

    [TestMethod]
    public void StrokeToArrows_EmptyInput_ReturnsEmpty()
    {
        Assert.AreEqual(string.Empty, DirectionExtensions.StrokeToArrows(""));
    }

    [TestMethod]
    public void ToArrow_MatchesEnum()
    {
        Assert.AreEqual('↑', Direction.Up.ToArrow());
        Assert.AreEqual('→', Direction.Right.ToArrow());
        Assert.AreEqual('↓', Direction.Down.ToArrow());
        Assert.AreEqual('←', Direction.Left.ToArrow());
    }
}
