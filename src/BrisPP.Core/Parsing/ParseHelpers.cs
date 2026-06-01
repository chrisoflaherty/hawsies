using System.Globalization;
using System.Text.RegularExpressions;
using BrisPP.Core.Model;

namespace BrisPP.Core.Parsing;

internal static partial class ParseHelpers
{
    public static long? Money(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var digits = new string(s.Where(char.IsDigit).ToArray());
        return digits.Length > 0 && long.TryParse(digits, out var v) ? v : null;
    }

    public static int? Int(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var m = IntRx().Match(s);
        return m.Success && int.TryParse(m.Value, out var v) ? v : null;
    }

    public static decimal? Decimal(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var m = DecimalRx().Match(s);
        return m.Success && decimal.TryParse(m.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var v)
            ? v : null;
    }

    /// <summary>Parse "5 0 - 0 - 1 $18,892 81" (commas may appear as spaces).</summary>
    public static RaceRecord? Record(string? s)
    {
        if (s is null) return null;
        var m = RecordRx().Match(s);
        if (!m.Success) return null;
        return new RaceRecord(
            Starts: int.Parse(m.Groups[1].Value),
            Wins: int.Parse(m.Groups[2].Value),
            Places: int.Parse(m.Groups[3].Value),
            Shows: int.Parse(m.Groups[4].Value),
            Earnings: Money(m.Groups[5].Value) ?? 0,
            BestSpeed: m.Groups[6].Success ? Int(m.Groups[6].Value) : null);
    }

    /// <summary>Parse a surface record like "Fst (112) 1 0 - 0 - 0 $1,375 59".</summary>
    public static SurfaceRecord? Surface(string? s)
    {
        if (s is null) return null;
        var m = SurfaceRx().Match(s);
        if (!m.Success) return null;
        var rec = Record(m.Groups[3].Value);
        if (rec is null) return null;
        return new SurfaceRecord(
            Surface: m.Groups[1].Value,
            ParFigure: m.Groups[2].Success ? Int(m.Groups[2].Value) : null,
            Record: rec);
    }

    public static readonly string[] MonthAbbr =
        { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };

    /// <summary>Parse a PP-style date like "11Apr26" or "27Dec'25" into a DateOnly.</summary>
    public static DateOnly? PpDate(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var m = PpDateRx().Match(s);
        if (!m.Success) return null;
        var day = int.Parse(m.Groups[1].Value);
        var monIdx = Array.FindIndex(MonthAbbr, x =>
            x.Equals(m.Groups[2].Value, StringComparison.OrdinalIgnoreCase));
        if (monIdx < 0) return null;
        var yy = int.Parse(m.Groups[3].Value);
        var year = 2000 + yy;
        try { return new DateOnly(year, monIdx + 1, day); }
        catch { return null; }
    }

    [GeneratedRegex(@"-?\d+")]
    private static partial Regex IntRx();

    [GeneratedRegex(@"-?\d+(\.\d+)?")]
    private static partial Regex DecimalRx();

    [GeneratedRegex(@"(\d+)\s+(\d+)\s*-?\s*(\d+)\s*-?\s*(\d+)\s+\$\s*([\d,\s]+)\s+(\d+)\b")]
    private static partial Regex RecordRx();

    [GeneratedRegex(@"([A-Za-z]{2,4})\s*\((\d+)\)\s*(.+)")]
    private static partial Regex SurfaceRx();

    [GeneratedRegex(@"(\d{1,2})([A-Za-z]{3})'?(\d{2})")]
    private static partial Regex PpDateRx();
}
