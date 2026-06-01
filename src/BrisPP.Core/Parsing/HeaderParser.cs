using System.Text.RegularExpressions;
using BrisPP.Core.Glyphs;
using BrisPP.Core.Layout;
using BrisPP.Core.Model;

namespace BrisPP.Core.Parsing;

/// <summary>
/// Parses the race header. The clean top line of each page carries
/// product / track / race-type / distance / age-sex / date / race-number;
/// the surrounding prose lines carry conditions, post times and PARS.
/// </summary>
public static partial class HeaderParser
{
    private const string ProductPrefix = "Premium Plus PP's";

    public sealed record TopLine(
        string Product, string Track, DateOnly? Date, int Number,
        string? RaceTypeCode, Distance? Distance, string? AgeSex);

    private static readonly HashSet<string> ClassPrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Mdn", "MC", "Mcl", "MSW", "OC", "Alw", "Aoc", "Clm", "Str", "Stk",
        "Hcp", "Wcl", "Wmc", "SOC", "Tr", "Trf", "Mdn.",
    };

    /// <summary>Parse the single top line of a page; null if it is not a race header.</summary>
    public static TopLine? ReadTopLine(string line)
    {
        if (line is null || !line.Contains("PP's")) return null;

        var product = ProductPrefix;
        var rest = line;
        var pIdx = rest.IndexOf("PP's", StringComparison.Ordinal);
        if (pIdx >= 0) rest = rest[(pIdx + 4)..].TrimStart();

        var numMatch = RaceNumberRx().Match(rest);
        if (!numMatch.Success) return null;
        var number = int.Parse(numMatch.Groups[1].Value);

        // Strip the trailing date + "Race N".
        var dateMatch = HeaderDateRx().Match(rest);
        DateOnly? date = null;
        string middle = rest[..numMatch.Index].Trim();
        if (dateMatch.Success)
        {
            date = BuildDate(dateMatch);
            middle = rest[..dateMatch.Index].Trim();
        }

        // middle = "<Track> <RaceType> <Distance> <AgeSex>".
        var tokens = middle.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int typeStart = FindRaceTypeStart(tokens);
        string track = string.Join(' ', tokens.Take(typeStart)).Trim();

        // Locate the distance unit (Mile / Furlongs) within the remainder.
        var afterTrack = string.Join(' ', tokens.Skip(typeStart));
        var distMatch = DistanceRx().Match(afterTrack);
        string? raceType = null;
        Distance? distance = null;
        string? ageSex = null;
        if (distMatch.Success)
        {
            var beforeUnit = afterTrack[..distMatch.Index].TrimEnd();
            // The distance value is the trailing token(s) before the unit word.
            var (typePart, valuePart) = SplitDistanceValue(beforeUnit);
            raceType = typePart;
            var unit = distMatch.Groups[1].Value;
            var turf = distMatch.Groups[2].Success;
            distance = BuildDistance(valuePart, unit, turf);
            ageSex = afterTrack[(distMatch.Index + distMatch.Length)..].Trim();
            if (ageSex.Length == 0) ageSex = null;
        }
        else
        {
            raceType = afterTrack.Trim();
        }

        return new TopLine(product, track, date, number, raceType, distance, ageSex);
    }

    /// <summary>Full header: top line plus conditions, purse, post times and PARS from the page.</summary>
    public static RaceHeader Parse(PageText page, int number)
    {
        var header = new RaceHeader();
        if (page.Lines.Count == 0) return header;

        var top = ReadTopLine(page.Lines[0].Text());
        if (top is not null)
        {
            header.Track = top.Track;
            header.Date = top.Date;
            header.RaceTypeCode = BrisGlyphs.Decode(top.RaceTypeCode ?? "");
            header.Distance = top.Distance;
            header.AgeSexConditions = top.AgeSex;
            (header.Grade, header.StakesName) = GradeAndStakes(top.RaceTypeCode);
            (header.Purse, header.PurseText) = PurseFrom(top.RaceTypeCode);
        }

        foreach (var line in page.Lines)
        {
            var text = line.Text();
            if (text.StartsWith("PostTime", StringComparison.OrdinalIgnoreCase))
            {
                var times = PostTimeRx().Matches(text).Select(m => m.Value).ToList();
                if (times.Count > 0) header.PostTimes = times;
            }
        }

        header.Conditions = ConditionsProse(page);
        return header;
    }

    private static int FindRaceTypeStart(string[] tokens)
    {
        for (int i = 0; i < tokens.Length; i++)
        {
            var t = tokens[i];
            if (i > 0 && IsRaceTypeToken(t)) return i;
        }
        return Math.Min(1, tokens.Length); // fallback: first token is track
    }

    private static bool IsRaceTypeToken(string t)
    {
        if (t.Length == 0) return false;
        if (t[0] == BrisGlyphs.StakesNameMarker) return true;
        if (GradeRx().IsMatch(t)) return true;
        if (ClassPrefixes.Contains(t.TrimEnd('.'))) return true;
        if (t.Any(char.IsDigit)) return true;
        return false;
    }

    private static (string TypePart, string ValuePart) SplitDistanceValue(string beforeUnit)
    {
        var tokens = beforeUnit.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return (beforeUnit, "");
        // The value is the last token (e.g. "1ˆ", "5½", "7"); a bare fraction
        // token ("½") also pulls in the preceding whole-number token ("6 ½").
        int valueStart = tokens.Length - 1;
        if (valueStart > 0 && IsLoneFraction(tokens[valueStart]) && IsDigits(tokens[valueStart - 1]))
            valueStart--;
        var value = string.Join(' ', tokens.Skip(valueStart));
        var type = string.Join(' ', tokens.Take(valueStart));
        return (type, value);
    }

    private static bool IsLoneFraction(string t) =>
        t.All(BrisGlyphs.IsFraction) && t.Length > 0;

    private static bool IsDigits(string t) => t.All(char.IsDigit) && t.Length > 0;

    private static Distance BuildDistance(string rawValue, string unit, bool turf)
    {
        var (whole, fractionGlyph) = SplitNumeric(rawValue);
        decimal value = whole + (fractionGlyph is char f ? BrisGlyphs.FractionValue(f) ?? 0m : 0m);
        decimal? furlongs = unit.StartsWith("Mile", StringComparison.OrdinalIgnoreCase)
            ? value * 8m : value;

        var fractionText = fractionGlyph is char fc ? " " + BrisGlyphs.Decode(fc.ToString()) : "";
        var display = $"{whole}{fractionText} {unit}{(turf ? " (T)" : "")}";
        var raw = $"{rawValue} {unit}{(turf ? " (T)" : "")}".Trim();

        return new Distance(raw)
        {
            Furlongs = furlongs,
            Surface = turf ? Surface.Turf : Surface.Dirt,
            Display = display,
        };
    }

    private static (int Whole, char? Fraction) SplitNumeric(string rawValue)
    {
        int whole = 0;
        char? fraction = null;
        foreach (var c in rawValue)
        {
            if (char.IsDigit(c)) whole = whole * 10 + (c - '0');
            else if (BrisGlyphs.IsFraction(c)) fraction = c;
        }
        return (whole, fraction);
    }

    private static (string? Grade, string? StakesName) GradeAndStakes(string? raceType)
    {
        if (string.IsNullOrEmpty(raceType)) return (null, null);
        var g = GradeRx().Match(raceType);
        string? grade = g.Success ? "G" + g.Groups[1].Value : null;
        bool isStakes = raceType[0] == BrisGlyphs.StakesNameMarker || grade is not null;
        string? name = null;
        if (isStakes)
        {
            name = raceType.TrimStart(BrisGlyphs.StakesNameMarker);
            if (g.Success) name = name[..g.Index].TrimEnd('-', ' ');
            name = BrisGlyphs.Decode(name);
        }
        return (grade, name);
    }

    private static (long?, string?) PurseFrom(string? raceType)
    {
        if (string.IsNullOrEmpty(raceType)) return (null, null);
        var m = PurseRx().Match(raceType);
        if (!m.Success) return (null, null);
        var n = long.Parse(m.Groups[1].Value);
        return (n * 1000, m.Value);
    }

    private static string? ConditionsProse(PageText page)
    {
        var prose = new List<string>();
        foreach (var line in page.Lines.Skip(1))
        {
            var t = line.Text().Trim();
            if (t.StartsWith("DATE TRK", StringComparison.OrdinalIgnoreCase)) break;
            if (IsProse(t)) prose.Add(t);
            if (prose.Count >= 3) break;
        }
        return prose.Count > 0 ? string.Join(' ', prose) : null;
    }

    private static bool IsProse(string t)
    {
        if (t.Length < 12) return false;
        int letters = t.Count(char.IsLetter);
        int spaces = t.Count(c => c == ' ');
        return letters > t.Length / 2 && spaces >= 2 && t.Any(char.IsUpper);
    }

    private static DateOnly? BuildDate(Match m)
    {
        var monIdx = Array.FindIndex(ParseHelpers.MonthAbbr, x =>
            x.Equals(m.Groups[1].Value, StringComparison.OrdinalIgnoreCase));
        if (monIdx < 0) return null;
        var day = int.Parse(m.Groups[2].Value);
        var year = int.Parse(m.Groups[3].Value);
        try { return new DateOnly(year, monIdx + 1, day); }
        catch { return null; }
    }

    [GeneratedRegex(@"Race\s+(\d+)\b")]
    private static partial Regex RaceNumberRx();

    [GeneratedRegex(@"(?:[A-Z][a-z]+,\s*)?([A-Z][a-z]{2})\s*0?(\d{1,2}),\s*(\d{4})")]
    private static partial Regex HeaderDateRx();

    [GeneratedRegex(@"\b(Mile|Furlongs?|Yards?)\b(?:\s*\((T)\))?")]
    private static partial Regex DistanceRx();

    [GeneratedRegex(@"-G(\d)\b")]
    private static partial Regex GradeRx();

    [GeneratedRegex(@"(\d+)k\b", RegexOptions.IgnoreCase)]
    private static partial Regex PurseRx();

    [GeneratedRegex(@"\d{1,2}:\d{2}")]
    private static partial Regex PostTimeRx();
}
