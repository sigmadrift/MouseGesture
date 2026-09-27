namespace MouseGesture.Core.Recognition;

/// <summary>Validation for stroke strings such as "URD".</summary>
public static class StrokeFormat
{
    /// <summary>Longest stroke the recognizer records and the editor accepts.</summary>
    public const int MaxLength = 8;

    /// <summary>
    /// Normalizes a stroke to upper case and validates it: 1..<see cref="MaxLength"/>
    /// characters from U/R/D/L with no direction repeated back-to-back (the recognizer
    /// never produces "UU"). Returns false for anything that could never be matched.
    /// </summary>
    public static bool TryNormalize(string? raw, out string stroke)
    {
        stroke = string.Empty;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var s = raw.Trim().ToUpperInvariant();
        if (s.Length > MaxLength)
            return false;
        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] is not ('U' or 'R' or 'D' or 'L'))
                return false;
            if (i > 0 && s[i] == s[i - 1])
                return false;
        }
        stroke = s;
        return true;
    }
}
