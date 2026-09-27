using MouseGesture.Core.Actions;
using MouseGesture.Core.Recognition;

namespace MouseGesture.Core.Tests;

[TestClass]
public sealed class GestureMapTests
{
    [TestMethod]
    public void Resolve_UnknownStroke_ReturnsNull()
    {
        var map = new GestureMap();
        var resolved = map.Resolve(new Gesture([Direction.Up]));
        Assert.IsNull(resolved);
    }

    [TestMethod]
    public void Bind_ThenResolve_ReturnsAction()
    {
        var map = new GestureMap();
        map.Bind("UR", BuiltInActions.NextDesktop);

        var resolved = map.Resolve(new Gesture([Direction.Up, Direction.Right]));
        Assert.AreSame(BuiltInActions.NextDesktop, resolved);
    }

    [TestMethod]
    public void Bind_OverwritesExistingStroke()
    {
        var map = new GestureMap();
        map.Bind("L", BuiltInActions.NextDesktop);
        map.Bind("L", BuiltInActions.PreviousDesktop);

        Assert.AreSame(BuiltInActions.PreviousDesktop, map.Resolve(new Gesture([Direction.Left])));
    }

    [TestMethod]
    public void Unbind_RemovesEntry()
    {
        var map = new GestureMap();
        map.Bind("D", BuiltInActions.ShowDesktop);

        Assert.IsTrue(map.Unbind("D"));
        Assert.IsNull(map.Resolve(new Gesture([Direction.Down])));
        Assert.IsFalse(map.Unbind("D"));
    }

    [TestMethod]
    public void IsBound_TrueWhenStrokeBound()
    {
        var map = new GestureMap();
        map.Bind("L", BuiltInActions.PreviousDesktop);

        Assert.IsTrue(map.IsBound("L"));
    }

    [TestMethod]
    public void IsBound_TrueForBothShortAndLongBindings()
    {
        var map = new GestureMap();
        map.Bind("L", BuiltInActions.PreviousDesktop);
        map.Bind("LR", BuiltInActions.NextDesktop);

        Assert.IsTrue(map.IsBound("L"));
        Assert.IsTrue(map.IsBound("LR"));
    }

    [TestMethod]
    public void IsBound_FalseWhenStrokeNotBound()
    {
        var map = new GestureMap();
        map.Bind("LR", BuiltInActions.NextDesktop);

        Assert.IsFalse(map.IsBound("L"));
        Assert.IsFalse(map.IsBound(""));
    }

    [TestMethod]
    public void CanFireEarly_FalseWhileLongerBindingReachable()
    {
        var map = new GestureMap();
        map.Bind("L", BuiltInActions.PreviousDesktop);
        map.Bind("LU", BuiltInActions.TaskView);
        map.Bind("R", BuiltInActions.NextDesktop);

        Assert.IsFalse(map.CanFireEarly("L"));
        Assert.IsTrue(map.CanFireEarly("LU"));
        Assert.IsTrue(map.CanFireEarly("R"));
        Assert.IsFalse(map.CanFireEarly("U"));
    }

    [TestMethod]
    public void HasLongerBinding_OnlyForRealPrefixes()
    {
        var map = new GestureMap();
        map.Bind("URD", BuiltInActions.TaskView);

        Assert.IsTrue(map.HasLongerBinding("U"));
        Assert.IsTrue(map.HasLongerBinding("UR"));
        Assert.IsFalse(map.HasLongerBinding("URD"));
        Assert.IsFalse(map.HasLongerBinding("R"));
    }

    [TestMethod]
    public void CreateDefault_ContainsFourCardinalBindings()
    {
        var map = GestureMap.CreateDefault();
        Assert.IsNotNull(map.Resolve(new Gesture([Direction.Left])));
        Assert.IsNotNull(map.Resolve(new Gesture([Direction.Right])));
        Assert.IsNotNull(map.Resolve(new Gesture([Direction.Up])));
        Assert.IsNotNull(map.Resolve(new Gesture([Direction.Down])));
    }
}
