namespace BrisPP.Core.Glyphs;

/// <summary>
/// Translation layer for the custom BRIS "PP2A" font used in Premium Plus Past
/// Performance PDFs. The font reuses Latin-1 code points as private glyphs:
/// superscript digits, fractions, and surface/condition markers. Raw glyph
/// strings are always preserved on the model; this class produces the decoded
/// forms.
/// </summary>
public static class BrisGlyphs
{
    // Superscript digit glyphs -> ASCII digit. Used for race-number-on-card,
    // jockey weight, fifths-of-a-second, and beaten-length integers.
    private static readonly Dictionary<char, char> SuperDigit = new()
    {
        ['§'] = '0', ['¨'] = '1', ['©'] = '2', ['ª'] = '3',
        ['«'] = '4', ['¬'] = '5', ['­'] = '6', ['®'] = '7',
        ['¯'] = '8', ['°'] = '9',
    };

    // Fraction glyphs -> (display, numeric value). Serve both distances
    // (1/16, 1/8, 1/4) and beaten-lengths (1/4, 1/2, 3/4).
    private static readonly Dictionary<char, (string Display, decimal Value)> Fraction = new()
    {
        ['ˆ'] = ("1/16", 0.0625m), // ˆ
        ['„'] = ("1/8", 0.125m),   // „
        ['‚'] = ("1/4", 0.25m),    // ‚
        ['½'] = ("1/2", 0.5m),     // ½
        ['ƒ'] = ("3/4", 0.75m),    // ƒ
    };

    public const char TurfMarker = 'à';        // à  turf course
    public const char AllWeatherMarker = 'Ì';  // Ì  synthetic / Tapeta
    public const char BulletMarker = '×';      // ×  bullet (best) workout
    public const char RaceFlagMarker = '¦';    // ¦  race condition flag (e.g. off-turf)
    public const char HorseFlagMarker = 'ì';   // ì  horse-level flag
    public const char StakesNameMarker = '™';  // ™  black-type stakes name
    public const char NeckMargin = '³';        // ³  sub-length winning margin (neck class)

    private static readonly HashSet<char> DroppedMarkers = new()
    {
        TurfMarker, AllWeatherMarker, RaceFlagMarker, HorseFlagMarker,
        StakesNameMarker, BulletMarker, '',
    };

    public static bool IsSuperDigit(char c) => SuperDigit.ContainsKey(c);
    public static bool IsFraction(char c) => Fraction.ContainsKey(c);

    /// <summary>Numeric value of a fraction glyph (e.g. '½' -> 0.5), or null.</summary>
    public static decimal? FractionValue(char c) =>
        Fraction.TryGetValue(c, out var f) ? f.Value : null;

    /// <summary>True if the string contains any custom BRIS glyph.</summary>
    public static bool HasGlyphs(string? s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        foreach (var c in s)
            if (SuperDigit.ContainsKey(c) || Fraction.ContainsKey(c) ||
                DroppedMarkers.Contains(c) || c == NeckMargin)
                return true;
        return false;
    }

    /// <summary>
    /// Human-readable cleaning: superscript digits become ASCII digits,
    /// fraction glyphs become "1/4" style text, surface/condition markers are
    /// dropped (they are captured structurally elsewhere). Use the typed
    /// decoders below when numeric meaning is required.
    /// </summary>
    public static string Decode(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return raw ?? string.Empty;
        var sb = new System.Text.StringBuilder(raw.Length);
        foreach (var c in raw)
        {
            if (SuperDigit.TryGetValue(c, out var d)) sb.Append(d);
            else if (Fraction.TryGetValue(c, out var f)) sb.Append(f.Display);
            else if (c == NeckMargin) sb.Append("nk");
            else if (DroppedMarkers.Contains(c)) { /* drop */ }
            else sb.Append(c);
        }
        return sb.ToString();
    }

    private static char? AsciiDigit(char c) => SuperDigit.TryGetValue(c, out var d) ? d : null;

    /// <summary>
    /// Convert a BRIS time token to total seconds. Times are minutes:seconds
    /// with the final fifth-of-a-second encoded as a trailing superscript digit
    /// (e.g. ":47¨" = 47.2, "1:41©" = 101.4, "1:11" = 71.0).
    /// </summary>
    public static decimal? FifthsToSeconds(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        decimal fifths = 0m;
        var core = raw.Trim();
        var last = core[^1];
        if (AsciiDigit(last) is char fd)
        {
            fifths = (fd - '0') / 5m;
            core = core[..^1];
        }
        core = core.TrimStart(':');
        if (core.Length == 0) return null;

        int minutes = 0;
        var colon = core.IndexOf(':');
        string secPart;
        if (colon >= 0)
        {
            if (!int.TryParse(core[..colon], out minutes)) return null;
            secPart = core[(colon + 1)..];
        }
        else secPart = core;

        if (!int.TryParse(secPart, out var seconds)) return null;
        return minutes * 60 + seconds + fifths;
    }

    /// <summary>
    /// Interpret a beaten-length token's superscript portion: leading
    /// superscript digits form the integer, an optional trailing fraction glyph
    /// adds the remainder. "¬‚" -> 5.25, "®ƒ" -> 7.75, "­" -> 6.
    /// </summary>
    public static decimal? LengthsToValue(string? superRaw)
    {
        if (string.IsNullOrEmpty(superRaw)) return null;
        decimal whole = 0m;
        decimal frac = 0m;
        bool any = false;
        foreach (var c in superRaw)
        {
            if (AsciiDigit(c) is char d) { whole = whole * 10 + (d - '0'); any = true; }
            else if (Fraction.TryGetValue(c, out var f)) { frac = f.Value; any = true; }
            else if (c == NeckMargin) { frac = 0.2m; any = true; }
        }
        return any ? whole + frac : null;
    }

    /// <summary>
    /// Split a running-line call token into running position (baseline digits)
    /// and lengths behind/ahead (superscript digits + fraction).
    /// "5¬‚" -> (5, 5.25); "7­" -> (7, 6); "3" -> (3, null); "10¨®" -> (10, 17).
    /// </summary>
    public static (int? Position, decimal? Lengths) ParseCall(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return (null, null);
        var s = raw.Trim();
        int i = 0;
        int pos = 0;
        bool posAny = false;
        while (i < s.Length && s[i] >= '0' && s[i] <= '9')
        {
            pos = pos * 10 + (s[i] - '0');
            posAny = true;
            i++;
        }
        var lengths = LengthsToValue(s[i..]);
        return (posAny ? pos : null, lengths);
    }
}
