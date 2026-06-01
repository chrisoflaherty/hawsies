using System.Globalization;
using BrisPP.Core.Glyphs;

namespace BrisPP.Core.Model;

public enum Surface { Unknown, Dirt, Turf, InnerTurf, AllWeather, InnerDirt }

/// <summary>A time encoded in BRIS fifths-of-a-second notation. Raw is preserved.</summary>
public sealed record RaceTime(string Raw)
{
    public decimal? Seconds => BrisGlyphs.FifthsToSeconds(Raw);

    public string Display
    {
        get
        {
            var s = Seconds;
            if (s is null) return BrisGlyphs.Decode(Raw);
            var total = s.Value;
            var minutes = (int)(total / 60);
            var sec = total - minutes * 60;
            return minutes > 0
                ? $"{minutes}:{sec.ToString("00.0", CultureInfo.InvariantCulture)}"
                : sec.ToString("0.0", CultureInfo.InvariantCulture);
        }
    }
}

/// <summary>A margin/beaten-length value (e.g. "3¾", "nk"). Raw is preserved.</summary>
public sealed record Margin(string Raw)
{
    public decimal? Lengths => BrisGlyphs.LengthsToValue(Raw);
    public string Display => BrisGlyphs.Decode(Raw);
}

/// <summary>A running-line call: position plus lengths behind/ahead. Raw is preserved.</summary>
public sealed record Call(string Raw)
{
    public int? Position => BrisGlyphs.ParseCall(Raw).Position;
    public decimal? Lengths => BrisGlyphs.ParseCall(Raw).Lengths;
    public string Display => BrisGlyphs.Decode(Raw);
}

/// <summary>A race distance. Furlongs/IsAbout/Surface are resolved by the parser.</summary>
public sealed record Distance(string Raw)
{
    public decimal? Furlongs { get; init; }
    public bool IsAbout { get; init; }
    public Surface Surface { get; init; } = Surface.Unknown;
    public string Display { get; init; } = "";
}

/// <summary>A win-place-show record with optional best speed figure.</summary>
public sealed record RaceRecord(
    int Starts, int Wins, int Places, int Shows, long Earnings, int? BestSpeed = null)
{
    public string? Label { get; init; }
}

/// <summary>A surface-specific record line, e.g. "Fst(112) 1 0-0-0 $1,375 59".</summary>
public sealed record SurfaceRecord(string Surface, int? ParFigure, RaceRecord Record);

/// <summary>BRIS pace / speed figures.</summary>
public sealed record PaceFigures
{
    public int? E1 { get; init; }
    public int? E2 { get; init; }
    public int? LatePace { get; init; }
    public int? FirstCall { get; init; }
    public int? SecondCall { get; init; }
    public int? Speed { get; init; }
}
