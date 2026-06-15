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
        if (File.Exists(_tempPath))
            File.Delete(_tempPath);
    }

    [TestMethod]
    public void LoadOrDefault_NoFile_ReturnsDefaultMap()
    {
        var store = new BindingStore(new ActionRegistry(), _tempPath);
        var map = store.LoadOrDefault();

        Assert.IsNotNull(map.Resolve(new Gesture([Direction.Left])));
    }

    [TestMethod]
    public void Save_ThenLoad_RoundTripsBindings()
    {
        var registry = new ActionRegistry();
        var store = new BindingStore(registry, _tempPath);

        var map = new GestureMap();
        map.Bind("UR", BuiltInActions.TaskView);
        map.Bind("D", BuiltInActions.ShowDesktop);
        store.Save(map);

        var reloaded = store.LoadOrDefault();
        Assert.AreSame(BuiltInActions.TaskView, reloaded.Resolve(new Gesture([Direction.Up, Direction.Right])));
        Assert.AreSame(BuiltInActions.ShowDesktop, reloaded.Resolve(new Gesture([Direction.Down])));
    }

    [TestMethod]
    public void Load_UnknownActionId_SkipsEntry()
    {
        File.WriteAllText(_tempPath, """
            {
              "bindings": [
                { "stroke": "L", "actionId": "DoesNotExist" },
                { "stroke": "R", "actionId": "NextDesktop" }
              ]
            }
            """);

        var store = new BindingStore(new ActionRegistry(), _tempPath);
        var map = store.LoadOrDefault();

        Assert.IsNull(map.Resolve(new Gesture([Direction.Left])));
        Assert.AreSame(BuiltInActions.NextDesktop, map.Resolve(new Gesture([Direction.Right])));
    }

    [TestMethod]
    public void Load_CorruptFile_FallsBackToDefault()
    {
        File.WriteAllText(_tempPath, "{ not valid json");

        var store = new BindingStore(new ActionRegistry(), _tempPath);
        var map = store.LoadOrDefault();

        // Default map has L bound.
        Assert.IsNotNull(map.Resolve(new Gesture([Direction.Left])));
    }
}
