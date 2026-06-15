namespace MouseGesture.Core.Recognition;

public enum Direction
{
    Up,
    Right,
    Down,
    Left,
}

public static class DirectionExtensions
{
    public static char ToChar(this Direction d) => d switch
    {
        Direction.Up => 'U',
        Direction.Right => 'R',
        Direction.Down => 'D',
        Direction.Left => 'L',
        _ => '?',
    };

    public static char ToArrow(this Direction d) => d switch
    {
        Direction.Up => '↑',     // ↑
        Direction.Right => '→',  // →
        Direction.Down => '↓',   // ↓
        Direction.Left => '←',   // ←
        _ => '?',
    };

    /// <summary>Converts a stroke string (e.g. "URD") to its arrow form ("↑→↓").</summary>
    public static string StrokeToArrows(string stroke)
    {
        if (string.IsNullOrEmpty(stroke))
            return string.Empty;
        return string.Create(stroke.Length, stroke, static (span, src) =>
        {
            for (var i = 0; i < src.Length; i++)
            {
                span[i] = src[i] switch
                {
                    'U' or 'u' => '↑',
                    'R' or 'r' => '→',
                    'D' or 'd' => '↓',
                    'L' or 'l' => '←',
                    _ => '?',
                };
            }
        });
    }
}
