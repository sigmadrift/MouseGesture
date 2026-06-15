using System.Collections.Immutable;
using MouseGesture.Core.Recognition;

namespace MouseGesture.Core.Tests;

[TestClass]
public sealed class GestureTests
{
    [TestMethod]
    public void Stroke_EmptyDirections_ReturnsEmptyString()
    {
        var g = new Gesture(ImmutableArray<Direction>.Empty);
        Assert.AreEqual("", g.Stroke);
    }

    [TestMethod]
    public void Stroke_DefaultArray_ReturnsEmptyString()
    {
        var g = new Gesture(default);
        Assert.AreEqual("", g.Stroke);
    }

    [TestMethod]
    public void Stroke_SingleDirection_ReturnsSingleChar()
    {
        var g = new Gesture([Direction.Right]);
        Assert.AreEqual("R", g.Stroke);
    }

    [TestMethod]
    public void Stroke_PreservesOrder()
    {
        var g = new Gesture([Direction.Up, Direction.Right, Direction.Down, Direction.Left]);
        Assert.AreEqual("URDL", g.Stroke);
    }

    [TestMethod]
    public void DirectionToChar_AllValues()
    {
        Assert.AreEqual('U', Direction.Up.ToChar());
        Assert.AreEqual('R', Direction.Right.ToChar());
        Assert.AreEqual('D', Direction.Down.ToChar());
        Assert.AreEqual('L', Direction.Left.ToChar());
    }
}
