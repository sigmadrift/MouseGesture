using System.Text.Json.Serialization;

namespace MouseGesture.Core.Persistence;

public sealed class BindingsConfig
{
    /// <summary>Mouse button held to start a gesture. Stored as the enum name (Right/Middle/XButton1/XButton2).</summary>
    public string Trigger { get; set; } = "Right";

    public List<BindingEntry> Bindings { get; set; } = new();

    /// <summary>Whether wheel amplification (modifier button + wheel) is enabled.</summary>
    public bool WheelAmplifyEnabled { get; set; } = true;

    /// <summary>Button held to amplify the wheel. Enum name (Right/Middle/XButton1/XButton2).</summary>
    public string WheelModifierButton { get; set; } = "XButton1";

    /// <summary>Wheel notches emitted per physical notch while the modifier is held.</summary>
    public int WheelMultiplier { get; set; } = 3;
}

public sealed class BindingEntry
{
    public string Stroke { get; set; } = "";
    public string ActionId { get; set; } = "";
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(BindingsConfig))]
internal partial class BindingsJsonContext : JsonSerializerContext
{
}
