using System.Text.RegularExpressions;
using BrisPP.Core.Glyphs;
using BrisPP.Core.Layout;
using BrisPP.Core.Model;

namespace BrisPP.Core.Parsing;

/// <summary>
/// Parses the running section of a horse block: past-performance lines,
/// workouts, trainer angles, jockey/trainer combos and "previously trained"
/// notes. Past-performance columns are aligned to fixed x positions taken from
/// the "DATE TRK DIST RACETYPE..." header ruler, so each cell can be sliced and
/// decoded independently.
/// </summary>
public static partial class RunningLineParser
{
    // Column left edges, calibrated against the page header glyph positions.
    private const double XDateTrk = 0, XMid = 52, XRaceType = 165;
    private const double XE1 = 216, XE2 = 228, XLp = 242, XP1c = 254, XP2c = 266;
    private const double XSpd = 278, XPp = 292, XSt = 301, XC1 = 315, XC2 = 330;
    private const double XStr = 345, XFin = 360, XJockey = 375, XMed = 420;
    private const double XOdds = 433, XTopFin = 452, XComment = 540, XField = 600, XEnd = 612;

    public static void Populate(PageText page, double headerBaseline, double blockBottom, Horse horse)
    {
        var lines = page.Lines
            .Where(l => l.Baseline > headerBaseline + 2 && l.Baseline < blockBottom)
            .OrderBy(l => l.Baseline);

        foreach (var line in lines)
        {
            var text = line.Text();
            if (text.StartsWith("Trainer:", StringComparison.OrdinalIgnoreCase))
                ParseTrainerLine(text, horse);
            else if (text.StartsWith("J/T", StringComparison.OrdinalIgnoreCase))
                ParseJtLine(text, horse);
            else if (text.StartsWith("Previously", StringComparison.OrdinalIgnoreCase))
                horse.Notes.Add(text.Trim());
            else if (WorkoutRx().IsMatch(text))
                horse.Workouts.AddRange(ParseWorkouts(text));
            else if (DateStartRx().IsMatch(text) && ParsePp(line) is { } pp)
                horse.PastPerformances.Add(pp);
        }
    }

    private static PastPerformanceLine? ParsePp(TextLine line)
    {
        var pp = new PastPerformanceLine();
        if (line.Text().Contains(BrisGlyphs.RaceFlagMarker))
            pp.RaceFlag = BrisGlyphs.RaceFlagMarker.ToString();

        ParseDateTrack(line.Slice(XDateTrk, XMid), pp);
        // A genuine PP row always carries a parseable date in its first column;
        // scatter-mangled workout lines that slip past WorkoutRx do not.
        if (pp.RawDate is null) return null;
        ParseMid(line.Slice(XMid, XRaceType), pp);
        pp.RaceType = Clean(line.Slice(XRaceType, XE1));

        pp.Figures = new PaceFigures
        {
            E1 = ParseHelpers.Int(line.Slice(XE1, XE2)),
            E2 = ParseHelpers.Int(line.Slice(XE2, XLp)),
            LatePace = ParseHelpers.Int(line.Slice(XLp, XP1c)),
            Speed = ParseHelpers.Int(line.Slice(XSpd, XPp)),
        };
        pp.PaceAdjust1c = ParseHelpers.Int(line.Slice(XP1c, XP2c));
        pp.PaceAdjust2c = ParseHelpers.Int(line.Slice(XP2c, XSpd));
        pp.Speed = pp.Figures.Speed;
        pp.PostPosition = ParseHelpers.Int(line.Slice(XPp, XSt));

        pp.Start = Call(line.Slice(XSt, XC1));
        pp.FirstCall = Call(line.Slice(XC1, XC2));
        pp.SecondCall = Call(line.Slice(XC2, XStr));
        pp.Stretch = Call(line.Slice(XStr, XFin));
        pp.Finish = Call(line.Slice(XFin, XJockey));

        ParseJockey(line.Slice(XJockey, XMed), pp);
        pp.MedicationEquipment = NullIfEmpty(Clean(line.Slice(XMed, XOdds)));
        ParseOdds(line.Slice(XOdds, XTopFin), pp);
        pp.TopFinishers = ParseFinishers(line.Slice(XTopFin, XComment));
        pp.Comment = NullIfEmpty(Clean(line.Slice(XComment, XField)));
        pp.FieldSize = ParseHelpers.Int(line.Slice(XField, XEnd));
        return pp;
    }

    private static void ParseDateTrack(string raw, PastPerformanceLine pp)
    {
        var m = PpDateTrackRx().Match(raw);
        if (!m.Success) return;
        pp.RawDate = m.Groups[1].Value;
        pp.Date = ParseHelpers.PpDate(m.Groups[1].Value);
        pp.Track = m.Groups[2].Value;
        var rest = BrisGlyphs.Decode(m.Groups[3].Value);
        if (int.TryParse(new string(rest.Where(char.IsDigit).ToArray()), out var rn))
            pp.TrackRaceNumber = rn;
    }

    private static void ParseMid(string raw, PastPerformanceLine pp)
    {
        if (raw.Contains(BrisGlyphs.TurfMarker)) pp.Surface = Surface.Turf;
        else if (raw.Contains(BrisGlyphs.AllWeatherMarker)) pp.Surface = Surface.AllWeather;
        else pp.Surface = Surface.Dirt;

        // Tokenize the raw glyph slice so the distance keeps its glyph boundaries
        // ("1ˆ" must stay "1 1/16", not collapse to an ambiguous "11/16").
        var tokens = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim(BrisGlyphs.TurfMarker, BrisGlyphs.AllWeatherMarker))
            .Where(t => t.Length > 0)
            .ToList();
        if (tokens.Count == 0) return;

        int i = 0;
        // Distance: the first digit-bearing token that isn't a split time.
        if (tokens[i].Any(char.IsDigit) && !tokens[i].Contains(':'))
        {
            pp.Distance = ParsePpDistance(tokens[i], pp.Surface);
            i++;
        }
        if (i < tokens.Count)
        {
            var cond = Clean(tokens[i]);
            if (ConditionRx().IsMatch(cond)) { pp.TrackCondition = cond; i++; }
        }
        for (; i < tokens.Count; i++)
            if (tokens[i].Any(char.IsDigit))
                pp.Fractions.Add(new RaceTime(tokens[i]));
    }

    /// <summary>
    /// Builds a Distance from a single raw PP token, keeping the glyph fraction
    /// distinct from the whole number ("1ˆ" -> 1 1/16 miles, "6f" -> 6 furlongs).
    /// Bare whole/fraction values with no unit letter are routes (miles).
    /// </summary>
    private static Distance ParsePpDistance(string token, Surface surface)
    {
        bool about = token.StartsWith('*');
        var t = about ? token[1..] : token;

        var whole = new System.Text.StringBuilder();
        var fracGlyphs = new System.Text.StringBuilder();
        decimal frac = 0;
        char unit = '\0';
        foreach (var c in t)
        {
            if (char.IsDigit(c) && fracGlyphs.Length == 0 && unit == '\0')
                whole.Append(c);
            else if (BrisGlyphs.FractionValue(c) is { } fv) { frac += fv; fracGlyphs.Append(c); }
            else if (c is 'f' or 'y' or 'm' or 'F' or 'Y' or 'M') unit = char.ToLowerInvariant(c);
        }

        int wholeVal = whole.Length > 0 ? int.Parse(whole.ToString()) : 0;
        decimal value = wholeVal + frac;
        char u = unit == '\0' ? 'm' : unit;
        decimal furlongs = u switch
        {
            'f' => value,
            'y' => value / 220m,
            _ => value * 8m,             // miles
        };

        var fracText = fracGlyphs.Length > 0 ? BrisGlyphs.Decode(fracGlyphs.ToString()) : "";
        var display = (about ? "*" : "") + wholeVal
            + (fracText.Length > 0 ? " " + fracText : "")
            + (unit == '\0' ? "m" : unit.ToString());

        return new Distance(token)
        {
            Surface = surface,
            Display = display,
            Furlongs = furlongs,
            IsAbout = about,
        };
    }

    private static void ParseJockey(string raw, PastPerformanceLine pp)
    {
        var decoded = BrisGlyphs.Decode(raw).Trim();
        var m = JockeyWeightRx().Match(decoded);
        if (m.Success)
        {
            pp.Jockey = m.Groups[1].Value.Trim();
            if (m.Groups[2].Success && int.TryParse(m.Groups[2].Value, out var w))
                pp.CarriedWeight = w;
        }
        else if (decoded.Length > 0) pp.Jockey = decoded;
    }

    private static void ParseOdds(string raw, PastPerformanceLine pp)
    {
        var decoded = BrisGlyphs.Decode(raw).Trim();
        if (decoded.Contains('*')) pp.Favorite = true;
        var m = OddsRx().Match(decoded);
        if (m.Success && decimal.TryParse(m.Value, out var o)) pp.Odds = o;
    }

    private static List<Finisher> ParseFinishers(string raw)
    {
        var finishers = new List<Finisher>();
        var name = new System.Text.StringBuilder();
        var margin = new System.Text.StringBuilder();

        void Flush()
        {
            if (name.Length == 0) return;
            var mg = margin.ToString();
            finishers.Add(new Finisher(
                BrisGlyphs.Decode(name.ToString()).Trim(),
                mg.Length > 0 ? new Margin(mg) : null));
            name.Clear();
            margin.Clear();
        }

        foreach (var c in raw)
        {
            if (c == ' ') continue;
            bool glyph = BrisGlyphs.IsSuperDigit(c) || BrisGlyphs.IsFraction(c) || c == BrisGlyphs.NeckMargin;
            if (glyph) margin.Append(c);
            else
            {
                if (margin.Length > 0) Flush(); // margin closed a finisher
                name.Append(c);
            }
        }
        Flush();
        return finishers;
    }

    private static IEnumerable<Workout> ParseWorkouts(string text)
    {
        // Split into one segment per workout before parsing. A single global
        // scan lets the trailing "rank/total" group swallow the next workout's
        // leading date digits ("50/78 28Mar" -> total 7828), so we isolate each
        // workout at its date boundary and parse the segments independently.
        foreach (var segment in WorkoutSplitRx().Split(text))
        {
            var m = WorkoutRx().Match(segment);
            if (!m.Success) continue;
            var w = new Workout
            {
                Bullet = m.Groups[1].Success,
                RawDate = m.Groups[2].Value,
                Date = ParseHelpers.PpDate(m.Groups[2].Value),
                Track = m.Groups[3].Value,
                Condition = m.Groups[6].Value,
                Time = new RaceTime(m.Groups[7].Value),
                Designation = m.Groups[8].Value,
            };
            var dist = $"{m.Groups[4].Value}{m.Groups[5].Value}";
            w.Distance = new Distance(dist) { Display = dist };
            if (m.Groups[9].Success)
            {
                var rank = m.Groups[9].Value.Split('/');
                w.Rank = Digits(rank[0]);
                if (rank.Length > 1) w.OutOf = Digits(rank[1]);
            }
            yield return w;
        }
    }

    private static void ParseTrainerLine(string text, Horse horse)
    {
        foreach (Match m in AngleRx().Matches(text))
        {
            horse.TrainerAngles.Add(new TrainerAngle
            {
                Category = m.Groups[1].Value.Trim(),
                Starts = Digits(m.Groups[2].Value),
                WinPercent = Digits(m.Groups[3].Value),
                Roi = ParseSigned(m.Groups[4].Value),
                Raw = m.Value.Trim(),
            });
        }
        ParseJtLine(text, horse);
    }

    private static void ParseJtLine(string text, Horse horse)
    {
        foreach (Match m in JtRx().Matches(text))
        {
            var stat = new JockeyTrainerStat
            {
                Starts = Digits(m.Groups[2].Value),
                WinRate = ParseSigned(m.Groups[3].Value),
                Roi = ParseSigned(m.Groups[4].Value),
                Raw = m.Value.Trim(),
            };
            if (m.Groups[1].Value.Contains("Meet", StringComparison.OrdinalIgnoreCase))
                horse.JtMeet = stat;
            else horse.JtLast365 = stat;
        }
    }

    private static Call? Call(string rawSlice)
    {
        var s = rawSlice.Trim();
        return s.Length == 0 ? null : new Call(s);
    }

    private static string Clean(string raw) => BrisGlyphs.Decode(raw).Trim();
    private static string? NullIfEmpty(string s) => s.Length == 0 ? null : s;

    private static int? Digits(string s)
    {
        var d = new string(s.Where(char.IsDigit).ToArray());
        return d.Length > 0 && int.TryParse(d, out var v) ? v : null;
    }

    private static decimal? ParseSigned(string s)
    {
        var cleaned = s.Replace(" ", "").Replace("$", "");
        return decimal.TryParse(cleaned, out var v) ? v : null;
    }

    [GeneratedRegex(@"^\s*[×]?\d{1,2}[A-Za-z]{3}'?\d{0,2}")]
    private static partial Regex DateStartRx();

    [GeneratedRegex(@"^\s*(\d{1,2}[A-Za-z]{3}'?\d{2})\s*([A-Za-z]+)(.*)$")]
    private static partial Regex PpDateTrackRx();

    [GeneratedRegex(@"^(ft|fm|gd|sy|sly|my|yl|wf|gs|fst|hy)$", RegexOptions.IgnoreCase)]
    private static partial Regex ConditionRx();

    [GeneratedRegex(@"^([A-Za-z'.\-]+?)\s*(\d+)?$")]
    private static partial Regex JockeyWeightRx();

    [GeneratedRegex(@"\*?(\d+(?:\.\d+)?)")]
    private static partial Regex OddsRx();

    [GeneratedRegex(@"(×)?(\d{1,2}[A-Za-z]{3}'?\d{0,2})\s*([A-Za-z]+)\s+(\d+)([fyY])\s+([A-Za-z]{2,3})\s+(\d?:?\d{2}[§¨©ª«¬­®¯°]?)\s+([BHGM][a-z]?)\b\s*([\d ]+/[\d ]+\d)?")]
    private static partial Regex WorkoutRx();

    // Boundary before each "date + track + distance" — splits a packed workout
    // row into one segment per workout so trailing ranks can't bleed across.
    [GeneratedRegex(@"(?<![\d×])(?=×?\d{1,2}[A-Za-z]{3}'?\d{0,2}[A-Za-z]{2,3}\s+\d+[fyY]\s)")]
    private static partial Regex WorkoutSplitRx();

    // Win% is 1-2 digits (a single scatter space tolerated); the lazy starts
    // group then resolves to everything before it.
    [GeneratedRegex(@"([A-Za-z][A-Za-z /0-9'.\-]*?)\(\s*([\d ]+?)\s+(\d\s?\d?)%\s+([+\-]?\$?[\d. ]+?)\s*\)")]
    private static partial Regex AngleRx();

    // Win-rate is a leading-dot decimal (".24"), which anchors the boundary so a
    // scattered 3-digit start count ("1 93") stays with the starts group.
    [GeneratedRegex(@"(J/T\s*(?:Meet|L365)):\s*\(\s*([\d ]+?)\s+(\.\s?\d\s?\d?)\s+([+\-]?\$?[\d. ]+?)\s*\)")]
    private static partial Regex JtRx();
}
