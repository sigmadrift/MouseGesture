using MouseGesture.Core.Hooks;
using MouseGesture.Core.Recognition;

namespace MouseGesture.Core.Tests;

[TestClass]
public sealed class WheelAmplifierTests
{
    private FakeMouseSource _source = null!;
    private RecordingSynthesizer _input = null!;
    private ManualTimeProvider _time = null!;
    private WheelAmplifier _amp = null!;

    [TestInitialize]
    public void Init()
    {
        _source = new FakeMouseSource();
        _input = new RecordingSynthesizer();
        _time = new ManualTimeProvider();
        _amp = new WheelAmplifier(_source, new ImmediateQueue(), _input, _time);
        _amp.Apply(new WheelSettings(true, TriggerButton.XButton1, 3));
    }

    [TestCleanup]
    public void Cleanup() => _amp.Dispose();

    private bool X1Down() => _source.Send(MouseEventType.XButtonDown, xButton: 1);
    private bool X1Up() => _source.Send(MouseEventType.XButtonUp, xButton: 1);

    [TestMethod]
    public void WheelWhileHeld_Amplified_NoClickOnRelease()
    {
        Assert.IsTrue(X1Down());
        Assert.IsTrue(_source.Send(MouseEventType.Wheel, wheelDelta: 120));
        Assert.IsTrue(X1Up());

        CollectionAssert.AreEqual(new[] { "wheel:120x3" }, _input.Calls);
    }

    [TestMethod]
    public void HorizontalWheel_Amplified()
    {
        X1Down();
        _source.Send(MouseEventType.HWheel, wheelDelta: -120);
        X1Up();

        CollectionAssert.AreEqual(new[] { "hwheel:-120x3" }, _input.Calls);
    }

    [TestMethod]
    public void HighResolutionWheel_CarriesRemainder()
    {
        X1Down();
        _source.Send(MouseEventType.Wheel, wheelDelta: 30); // 90 → 0 notches, carry 90
        _source.Send(MouseEventType.Wheel, wheelDelta: 30); // 90+90 → 1 notch, carry 60
        X1Up();

        CollectionAssert.AreEqual(new[] { "wheel:120x1" }, _input.Calls);
    }

    [TestMethod]
    public void Remainder_ResetBetweenHolds()
    {
        X1Down();
        _source.Send(MouseEventType.Wheel, wheelDelta: 30); // carry 90
        X1Up();
        X1Down();
        _source.Send(MouseEventType.Wheel, wheelDelta: 10); // 30 alone; stale 90 must not add up to a notch
        X1Up();

        Assert.IsEmpty(_input.Calls);
    }

    [TestMethod]
    public void PressWithoutWheel_Clicks()
    {
        X1Down();
        X1Up();
        CollectionAssert.AreEqual(new[] { "click:XButton1" }, _input.Calls);
    }

    [TestMethod]
    public void LongHoldWithoutWheel_PassesThrough()
    {
        _amp.HoldTimeoutMs = 400;
        X1Down();
        _time.Advance(TimeSpan.FromMilliseconds(400));
        CollectionAssert.AreEqual(new[] { "down:XButton1" }, _input.Calls);

        Assert.IsFalse(_source.Send(MouseEventType.Wheel, wheelDelta: 120), "no amplification after passthrough");
        Assert.IsTrue(X1Up());
        CollectionAssert.AreEqual(new[] { "down:XButton1", "up:XButton1" }, _input.Calls);
    }

    [TestMethod]
    public void MultiplierOne_DoesNotCaptureButton()
    {
        _amp.Multiplier = 1;
        Assert.IsFalse(X1Down());
        Assert.IsFalse(_source.Send(MouseEventType.Wheel, wheelDelta: 120));
        Assert.IsFalse(X1Up());
        Assert.IsEmpty(_input.Calls);
    }

    [TestMethod]
    public void DisabledMidHold_StillBalancesUp()
    {
        X1Down();
        _amp.IsEnabled = false;
        Assert.IsFalse(_source.Send(MouseEventType.Wheel, wheelDelta: 120), "wheel passes once disabled");
        Assert.IsTrue(X1Up());
        CollectionAssert.AreEqual(new[] { "click:XButton1" }, _input.Calls);
    }

    [TestMethod]
    public void OtherSideButton_Ignored()
    {
        Assert.IsFalse(_source.Send(MouseEventType.XButtonDown, xButton: 2));
        Assert.IsFalse(_source.Send(MouseEventType.XButtonUp, xButton: 2));
    }

    [TestMethod]
    public void Bypass_LeavesPressUntouched()
    {
        _amp.ShouldBypass = (_, _) => true;
        Assert.IsFalse(X1Down());
        Assert.IsFalse(_source.Send(MouseEventType.Wheel, wheelDelta: 120));
        Assert.IsFalse(X1Up());
    }
}
