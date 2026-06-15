using MouseGesture.Core.Actions;
using MouseGesture.Core.Persistence;
using MouseGesture.Core.Recognition;

namespace MouseGesture.Core.Tests;

[TestClass]
public sealed class TriggerPersistenceTests
{
    private string _tempPath = "";

    [TestInitialize]
    public void Init()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), $"mg-trigger-{Guid.NewGuid():N}.json");
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (File.Exists(_tempPath))
            File.Delete(_tempPath);
    }

    [TestMethod]
    public void LoadTrigger_NoFile_ReturnsRight()
    {
        var store = new BindingStore(new ActionRegistry(), _tempPath);
        Assert.AreEqual(TriggerButton.Right, store.LoadTrigger());
    }

    [TestMethod]
    public void Save_ThenLoadTrigger_RoundTrips()
    {
        var store = new BindingStore(new ActionRegistry(), _tempPath);
        store.Save(new GestureMap(), TriggerButton.XButton2);

        Assert.AreEqual(TriggerButton.XButton2, store.LoadTrigger());
    }

    [TestMethod]
    public void Save_PreservesTriggerOnLegacySave()
    {
        var store = new BindingStore(new ActionRegistry(), _tempPath);
        store.Save(new GestureMap(), TriggerButton.Middle);

        // Legacy single-arg save should preserve the previously stored trigger.
        var map = new GestureMap();
        map.Bind("U", BuiltInActions.TaskView);
        store.Save(map);

        Assert.AreEqual(TriggerButton.Middle, store.LoadTrigger());
    }

    [TestMethod]
    public void LoadTrigger_UnknownValue_FallsBackToRight()
    {
        File.WriteAllText(_tempPath, """
            { "trigger": "Foobar", "bindings": [] }
            """);
        var store = new BindingStore(new ActionRegistry(), _tempPath);
        Assert.AreEqual(TriggerButton.Right, store.LoadTrigger());
    }

    [TestMethod]
    public void LoadWheelSettings_NoFile_ReturnsDefault()
    {
        var store = new BindingStore(new ActionRegistry(), _tempPath);
        Assert.AreEqual(WheelSettings.Default, store.LoadWheelSettings());
    }

    [TestMethod]
    public void SaveWheel_ThenLoad_RoundTrips()
    {
        var store = new BindingStore(new ActionRegistry(), _tempPath);
        var wheel = new WheelSettings(Enabled: false, TriggerButton.XButton2, Multiplier: 5);
        store.Save(new GestureMap(), TriggerButton.Right, wheel);

        Assert.AreEqual(wheel, store.LoadWheelSettings());
    }

    [TestMethod]
    public void SaveWheel_ClampsMultiplier()
    {
        var store = new BindingStore(new ActionRegistry(), _tempPath);
        store.Save(new GestureMap(), TriggerButton.Right, new WheelSettings(true, TriggerButton.XButton1, 999));

        Assert.AreEqual(WheelSettings.MaxMultiplier, store.LoadWheelSettings().Multiplier);
    }

    [TestMethod]
    public void SaveWheel_CoercesInvalidModifierToSideButton()
    {
        var store = new BindingStore(new ActionRegistry(), _tempPath);
        // Middle/Right are not valid wheel modifiers; they must coerce to the default side button.
        store.Save(new GestureMap(), TriggerButton.Right, new WheelSettings(true, TriggerButton.Middle, 3));

        Assert.AreEqual(WheelSettings.Default.Modifier, store.LoadWheelSettings().Modifier);
    }

    [TestMethod]
    public void Save_MapAndTrigger_PreservesWheelSettings()
    {
        var store = new BindingStore(new ActionRegistry(), _tempPath);
        var wheel = new WheelSettings(Enabled: false, TriggerButton.XButton2, Multiplier: 7);
        store.Save(new GestureMap(), TriggerButton.Right, wheel);

        // A map/trigger-only save must not reset wheel settings to defaults.
        var map = new GestureMap();
        map.Bind("U", BuiltInActions.TaskView);
        store.Save(map, TriggerButton.XButton1);

        Assert.AreEqual(wheel, store.LoadWheelSettings());
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
