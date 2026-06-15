using System.Collections.Immutable;

namespace MouseGesture.Core.Recognition;

public sealed record Gesture(ImmutableArray<Direction> Directions)
{
    public string Stroke
    {
        get
        {
            if (Directions.IsDefaultOrEmpty)
                return string.Empty;
            return string.Create(Directions.Length, Directions, static (span, dirs) =>
            {
                for (var i = 0; i < dirs.Length; i++)
                    span[i] = dirs[i].ToChar();
            });
        }
    }

    public override string ToString() => Stroke;
}
