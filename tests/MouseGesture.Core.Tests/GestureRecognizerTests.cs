using MouseGesture.Core.Actions;
using MouseGesture.Core.Hooks;
using MouseGesture.Core.Recognition;
using R3;

namespace MouseGesture.Core.Tests;

[TestClass]
public sealed class GestureRecognizerTests
{
    private FakeMouseSource _source = null!;
    private RecordingSynthesizer _input = null!;
    private ManualTimeProvider _time = null!;
    private GestureRecognizer _recognizer = null!;
    private List<string> _recognized = null!;

    [TestInitialize]
    public void Init()
    {
        _source = new FakeMouseSource();
        _input = new RecordingSynthesizer();
        _time = new ManualTimeProvider();
        _recognizer = new GestureRecognizer(_source, new ImmediateQueue(), _input, _time);
        _recognized = new List<string>();
        _recognizer.Recognized.Subscribe(g => _recognized.Add(g.Stroke));
    }

    [TestCleanup]
    public void Cleanup() => _recognizer.Dispose();

    private bool Down() => _source.Send(MouseEventType.RightDown, 0, 0);
    private bool Up(int x = 0, int y = 0) => _source.Send(MouseEventType.RightUp, x, y);

    [TestMethod]
    public void MultiStroke_RecognizedOnRelease()
    {
        Assert.IsTrue(Down());
        _source.Move(0, -40);   // U
        _source.Move(40, -40);  // R
        _source.Move(40, 0);    // D
        Assert.IsTrue(Up(40, 0));

        CollectionAssert.AreEqual(new[] { "URD" }, _recognized);
        Assert.IsEmpty(_input.Calls, "a gesture must not also click");
    }

    [TestMethod]
    public void SameDirectionContinued_RecordedOnce()
    {
        Down();
        _source.Move(40, 0);
        _source.Move(80, 0);
        _source.Move(120, 0);
        Up(120, 0);

        CollectionAssert.AreEqual(new[] { "R" }, _recognized);
    }

    [TestMethod]
    public void StrokeLength_CappedAtMax()
    {
        Down();
        var x = 0;
        for (var i = 0; i < 12; i++)
        {
            x += i % 2 == 0 ? 40 : -40;
            _source.Move(x, 0);
        }
        Up(x, 0);

        Assert.AreEqual(StrokeFormat.MaxLength, _recognized.Single().Length);
    }

    [TestMethod]
    public void EarlyRecognize_FiresWithoutRelease_AndIgnoresRest()
    {
        _recognizer.EarlyRecognize = s => s == "U";
        Down();
        _source.Move(0, -40);
        CollectionAssert.AreEqual(new[] { "U" }, _recognized);

        _source.Move(40, -40);
        Up(40, -40);
        CollectionAssert.AreEqual(new[] { "U" }, _recognized, "nothing more after an early fire");
        Assert.IsEmpty(_input.Calls);
    }

    [TestMethod]
    public void EarlyRecognize_WithMap_WaitsWhenLongerBindingExists()
    {
        var map = new GestureMap();
        map.Bind("L", BuiltInActions.PreviousDesktop);
        map.Bind("LU", BuiltInActions.TaskView);
        _recognizer.EarlyRecognize = map.CanFireEarly;

        Down();
        _source.Move(-40, 0); // L — "LU" still possible, so no early fire
        Assert.IsEmpty(_recognized);
        _source.Move(-40, -40); // U — "LU" has no extensions: fires now
        CollectionAssert.AreEqual(new[] { "LU" }, _recognized);
        Up(-40, -40);
        Assert.HasCount(1, _recognized);
    }

    [TestMethod]
    public void PlainClick_SynthesizesClick()
    {
        Assert.IsTrue(Down());
        Assert.IsTrue(Up());

        Assert.IsEmpty(_recognized);
        CollectionAssert.AreEqual(new[] { "click:Right" }, _input.Calls);
    }

    [TestMethod]
    public void ShakyClickBelowSegmentDistance_StillClicks()
    {
        Down();
        _source.Move(15, 5); // past the dead zone, short of a full segment
        Up(15, 5);

        Assert.IsEmpty(_recognized);
        CollectionAssert.AreEqual(new[] { "click:Right" }, _input.Calls);
    }

    [TestMethod]
    public void HoldWithoutMoving_PassesThroughAsDownThenUp()
    {
        _recognizer.HoldTimeoutMs = 500;
        Down();
        _time.Advance(TimeSpan.FromMilliseconds(499));
        Assert.IsEmpty(_input.Calls);
        _time.Advance(TimeSpan.FromMilliseconds(1));
        CollectionAssert.AreEqual(new[] { "down:Right" }, _input.Calls);

        // Movement during a passthrough hold is a normal drag, not a gesture.
        _source.Move(200, 0);
        Assert.IsTrue(Up(200, 0), "real UP is replaced by a synthetic one to keep ordering");
        CollectionAssert.AreEqual(new[] { "down:Right", "up:Right" }, _input.Calls);
        Assert.IsEmpty(_recognized);
    }

    [TestMethod]
    public void MovingBeforeTimeout_CancelsPassthrough()
    {
        _recognizer.HoldTimeoutMs = 500;
        Down();
        _source.Move(20, 0); // past dead zone
        _time.Advance(TimeSpan.FromSeconds(2));
        _source.Move(40, 0);
        Up(40, 0);

        Assert.IsEmpty(_input.Calls);
        CollectionAssert.AreEqual(new[] { "R" }, _recognized);
    }

    [TestMethod]
    public void HoldTimeoutZero_NeverPassesThrough()
    {
        _recognizer.HoldTimeoutMs = 0;
        Down();
        _time.Advance(TimeSpan.FromSeconds(10));
        Up();

        CollectionAssert.AreEqual(new[] { "click:Right" }, _input.Calls);
    }

    [TestMethod]
    public void DisableMidHold_SwallowsUp_NoClick()
    {
        Down();
        _recognizer.IsEnabled = false;
        Assert.IsTrue(Up(), "the UP of a swallowed DOWN must not leak to apps");
        Assert.IsEmpty(_input.Calls);
        Assert.IsEmpty(_recognized);

        // Afterwards everything passes through untouched.
        Assert.IsFalse(Down());
        Assert.IsFalse(Up());
    }

    [TestMethod]
    public void TriggerChangedMidHold_OldButtonUpStillBalanced()
    {
        Down();
        _recognizer.Trigger = TriggerButton.Middle;
        Assert.IsTrue(Up());
        Assert.IsTrue(_source.Send(MouseEventType.MiddleDown));
        Assert.IsTrue(_source.Send(MouseEventType.MiddleUp));
        CollectionAssert.AreEqual(new[] { "click:Middle" }, _input.Calls);
    }

    [TestMethod]
    public void Bypass_LeavesPressUntouched()
    {
        _recognizer.ShouldBypass = (_, _) => true;
        Assert.IsFalse(Down());
        _source.Move(0, -100);
        Assert.IsFalse(Up());
        Assert.IsEmpty(_recognized);
        Assert.IsEmpty(_input.Calls);
    }

    [TestMethod]
    public void DpiScale_MultipliesDistances()
    {
        _recognizer.DpiScale = (_, _) => 2.0; // segment becomes 60px
        Down();
        _source.Move(40, 0);
        Up(40, 0);
        CollectionAssert.AreEqual(new[] { "click:Right" }, _input.Calls, "40px is below a 2x segment");
    }
}
