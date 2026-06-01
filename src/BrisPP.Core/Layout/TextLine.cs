using System.Text;
using BrisPP.Core.Pdf;

namespace BrisPP.Core.Layout;

/// <summary>A logical line of glyphs sharing a baseline, ordered left to right.</summary>
public sealed class TextLine
{
    public double Baseline { get; }
    public IReadOnlyList<PositionedChar> Chars { get; }

    public TextLine(double baseline, IReadOnlyList<PositionedChar> chars)
    {
        Baseline = baseline;
        Chars = chars;
    }

    public double Left => Chars.Count == 0 ? 0 : Chars[0].Left;
    public double Right => Chars.Count == 0 ? 0 : Chars[^1].Right;

    /// <summary>Full line text, inserting a single space where glyphs are separated by a gap.</summary>
    public string Text() => Render(Chars);

    /// <summary>Text of glyphs whose center X falls within [xMin, xMax).</summary>
    public string Slice(double xMin, double xMax)
    {
        var inRange = new List<PositionedChar>();
        foreach (var c in Chars)
            if (c.CenterX >= xMin && c.CenterX < xMax)
                inRange.Add(c);
        return Render(inRange);
    }

    private static string Render(IReadOnlyList<PositionedChar> chars)
    {
        if (chars.Count == 0) return string.Empty;
        var sb = new StringBuilder();
        for (int i = 0; i < chars.Count; i++)
        {
            if (i > 0)
            {
                var gap = chars[i].Left - chars[i - 1].Right;
                var threshold = 0.25 * Math.Max(chars[i].FontSize, chars[i - 1].FontSize);
                if (gap > threshold) sb.Append(' ');
            }
            sb.Append(chars[i].Text);
        }
        return sb.ToString();
    }
}

/// <summary>All glyphs and reconstructed lines for one page.</summary>
public sealed class PageText
{
    public int Number { get; }
    public double Width { get; }
    public double Height { get; }

    /// <summary>Full-width logical rows (header, PP lines, workouts, angle lines).</summary>
    public IReadOnlyList<TextLine> Lines { get; }

    /// <summary>All non-whitespace glyphs, for targeted rectangle/strip queries.</summary>
    public IReadOnlyList<PositionedChar> Chars { get; }

    public PageText(int number, double width, double height,
        IReadOnlyList<PositionedChar> chars, IReadOnlyList<TextLine> lines)
    {
        Number = number;
        Width = width;
        Height = height;
        Chars = chars;
        Lines = lines;
    }

    /// <summary>
    /// Cluster the glyphs inside a vertical column strip into sub-rows by baseline.
    /// Within a single column the competing-column interleave is gone, so a tight
    /// baseline tolerance cleanly separates the half-lines of the breeding grid.
    /// </summary>
    public IReadOnlyList<TextLine> RowsInStrip(
        double xMin, double xMax, double yMin, double yMax, double baselineTolerance = 2.5)
    {
        var selected = new List<PositionedChar>();
        foreach (var c in Chars)
            if (c.CenterX >= xMin && c.CenterX < xMax && c.Baseline >= yMin && c.Baseline <= yMax)
                selected.Add(c);
        return LineBuilder.BuildLinesByBaseline(selected, baselineTolerance);
    }
}

/// <summary>
/// Groups positioned glyphs into logical lines by vertical-rectangle overlap.
/// The overlap threshold is scaled to the smaller of the two glyph heights, so
/// raised superscripts (race numbers, fifths, beaten-lengths) and large-font
/// fractions stay attached to their line regardless of font size, while the
/// tightly stacked half-lines of the breeding grid (Sire / Dam) — which barely
/// overlap — are kept on separate rows.
/// </summary>
public static class LineBuilder
{
    private const double DefaultOverlapRatio = 0.2;

    private sealed class Band
    {
        public double Top;
        public double Bottom;
        public readonly List<PositionedChar> Chars = new();
        public double Height => Bottom - Top;
    }

    public static IReadOnlyList<TextLine> BuildLines(
        IReadOnlyList<PositionedChar> chars, double minOverlapRatio = DefaultOverlapRatio)
    {
        var lines = new List<TextLine>();
        if (chars.Count == 0) return lines;

        var sorted = chars.OrderBy(c => c.Top).ThenBy(c => c.Left).ToList();
        var bands = new List<Band>();

        foreach (var c in sorted)
        {
            Band? best = null;
            double bestOverlap = 0;
            for (int i = bands.Count - 1; i >= 0; i--)
            {
                var b = bands[i];
                if (b.Bottom <= c.Top) continue; // band entirely above this glyph
                var overlap = Math.Min(b.Bottom, c.Bottom) - Math.Max(b.Top, c.Top);
                if (overlap <= 0) continue;
                var threshold = minOverlapRatio * Math.Min(c.Height, b.Height);
                if (overlap >= threshold && overlap > bestOverlap)
                {
                    best = b;
                    bestOverlap = overlap;
                }
            }

            if (best is null)
            {
                best = new Band { Top = c.Top, Bottom = c.Bottom };
                bands.Add(best);
            }
            else
            {
                best.Top = Math.Min(best.Top, c.Top);
                best.Bottom = Math.Max(best.Bottom, c.Bottom);
            }
            best.Chars.Add(c);
        }

        foreach (var b in bands.OrderBy(b => b.Bottom))
            lines.Add(Finish(b.Chars));
        return lines;
    }

    /// <summary>
    /// Cluster glyphs into rows purely by baseline proximity. Only safe within a
    /// single column strip (see <see cref="PageText.RowsInStrip"/>), where there
    /// are no competing columns to interleave.
    /// </summary>
    public static IReadOnlyList<TextLine> BuildLinesByBaseline(
        IReadOnlyList<PositionedChar> chars, double tolerance)
    {
        var lines = new List<TextLine>();
        if (chars.Count == 0) return lines;

        var sorted = chars.OrderBy(c => c.Baseline).ThenBy(c => c.Left).ToList();
        var current = new List<PositionedChar>();
        double anchor = sorted[0].Baseline;
        foreach (var c in sorted)
        {
            if (current.Count > 0 && Math.Abs(c.Baseline - anchor) > tolerance)
            {
                lines.Add(Finish(current));
                current = new List<PositionedChar>();
            }
            if (current.Count == 0) anchor = c.Baseline;
            current.Add(c);
        }
        if (current.Count > 0) lines.Add(Finish(current));
        return lines;
    }

    private static TextLine Finish(List<PositionedChar> chars)
    {
        chars.Sort((a, b) => a.Left.CompareTo(b.Left));
        var baseline = chars.Max(c => c.Baseline);
        return new TextLine(baseline, chars);
    }
}
