using MouseGesture.Core.Actions;
using MouseGesture.Core.Persistence;
using MouseGesture.Core.Recognition;

namespace MouseGesture.Core.Tests;

[TestClass]
public sealed class BindingStoreTests
{
    private string _tempPath = "";

    [TestInitialize]
    public void Init()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), $"mg-bindings-{Guid.NewGuid():N}.json");
    }

    [TestCleanup]
    public void Cleanup()
    {
        foreach (var f in Directory.EnumerateFiles(Path.GetDirectoryName(_tempPath)!, Path.GetFileName(_tempPath) + "*"))
            File.Delete(f);
    }

    private BindingStore NewStore() => new(new ActionRegistry(), _tempPath);

    [TestMethod]
    public void Load_NoFile_ReturnsDefaults()
    {
        var result = NewStore().Load();

        Assert.IsNotNull(result.Map.Resolve(new Gesture([Direction.Left])));
        Assert.AreEqual(AppSettings.Default.Trigger, result.Settings.Trigger);
        Assert.AreEqual(AppSettings.Default.Wheel, result.Settings.Wheel);
        Assert.IsNull(result.CorruptBackupPath);
    }

    [TestMethod]
    public void Save_ThenLoad_RoundTripsBindingsAndSettings()
    {
        var store = NewStore();
        var map = new GestureMap();
        map.Bind("UR", BuiltInActions.TaskView);
        map.Bind("D", BuiltInActions.ShowDesktop);
        var settings = new AppSettings(
            TriggerButton.Middle,
            new WheelSettings(false, TriggerButton.XButton2, 7),
            HoldTimeoutMs: 800,
            DisableInFullscreen: false,
            ExcludedApps: ["game.exe", "other.exe"]);
        store.Save(map, settings);

        var reloaded = store.Load();
        Assert.AreSame(BuiltInActions.TaskView, reloaded.Map.Resolve(new Gesture([Direction.Up, Direction.Right])));
        Assert.AreSame(BuiltInActions.ShowDesktop, reloaded.Map.Resolve(new Gesture([Direction.Down])));
        Assert.AreEqual(TriggerButton.Middle, reloaded.Settings.Trigger);
        Assert.AreEqual(settings.Wheel, reloaded.Settings.Wheel);
        Assert.AreEqual(800, reloaded.Settings.HoldTimeoutMs);
        Assert.IsFalse(reloaded.Settings.DisableInFullscreen);
        CollectionAssert.AreEqual(new[] { "game.exe", "other.exe" }, reloaded.Settings.ExcludedApps.ToArray());
    }

    [TestMethod]
    public void Load_InvalidEntries_Skipped_AndStrokeNormalized()
    {
        File.WriteAllText(_tempPath, """
            {
              "bindings": [
                { "stroke": "L", "actionId": "DoesNotExist" },
                { "stroke": "UU", "actionId": "TaskView" },
                { "stroke": "X", "actionId": "TaskView" },
                { "stroke": "ur", "actionId": "TaskView" },
                { "stroke": "R", "actionId": "NextDesktop" }
              ]
            }
            """);

        var result = NewStore().Load();

        Assert.AreEqual(3, result.SkippedEntries);
        Assert.IsNull(result.Map.Resolve(new Gesture([Direction.Left])));
        Assert.AreSame(BuiltInActions.TaskView, result.Map.Resolve(new Gesture([Direction.Up, Direction.Right])));
        Assert.AreSame(BuiltInActions.NextDesktop, result.Map.Resolve(new Gesture([Direction.Right])));
    }

    [TestMethod]
    public void Load_CorruptFile_BacksUpAndFallsBackToDefault()
    {
        File.WriteAllText(_tempPath, "{ not valid json");

        var result = NewStore().Load();

        Assert.IsNotNull(result.Map.Resolve(new Gesture([Direction.Left])));
        Assert.IsNotNull(result.CorruptBackupPath);
        Assert.AreEqual("{ not valid json", File.ReadAllText(result.CorruptBackupPath));
    }

    [TestMethod]
    public void Load_UnknownTrigger_FallsBackToRight()
    {
        File.WriteAllText(_tempPath, """{ "trigger": "Foobar", "bindings": [] }""");
        Assert.AreEqual(TriggerButton.Right, NewStore().Load().Settings.Trigger);
    }

    [TestMethod]
    public void Load_NumericTrigger_OutOfRange_FallsBackToRight()
    {
        File.WriteAllText(_tempPath, """{ "trigger": "42", "bindings": [] }""");
        Assert.AreEqual(TriggerButton.Right, NewStore().Load().Settings.Trigger);
    }

    [TestMethod]
    public void Load_OldFileWithoutNewFields_UsesDefaults()
    {
        File.WriteAllText(_tempPath, """{ "trigger": "Right", "bindings": [], "wheelMultiplier": 5 }""");
        var s = NewStore().Load().Settings;

        Assert.AreEqual(5, s.Wheel.Multiplier);
        Assert.AreEqual(AppSettings.Default.HoldTimeoutMs, s.HoldTimeoutMs);
        Assert.AreEqual(AppSettings.Default.DisableInFullscreen, s.DisableInFullscreen);
        Assert.IsEmpty(s.ExcludedApps);
    }

    [TestMethod]
    public void Save_NormalizesWheelAndExcludedApps()
    {
        var store = NewStore();
        store.Save(new GestureMap(), AppSettings.Default with
        {
            Wheel = new WheelSettings(true, TriggerButton.Middle, 999),
            HoldTimeoutMs = -5,
            ExcludedApps = [" C:\\Games\\Game.EXE ", "game.exe", "notepad", ""],
        });

        var s = store.Load().Settings;
        Assert.AreEqual(WheelSettings.MaxMultiplier, s.Wheel.Multiplier);
        Assert.AreEqual(WheelSettings.Default.Modifier, s.Wheel.Modifier);
        Assert.AreEqual(0, s.HoldTimeoutMs);
        CollectionAssert.AreEqual(new[] { "game.exe", "notepad.exe" }, s.ExcludedApps.ToArray());
    }
}
