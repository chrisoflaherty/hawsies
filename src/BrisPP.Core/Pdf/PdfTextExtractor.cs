using BrisPP.Core.Layout;
using UglyToad.PdfPig;

namespace BrisPP.Core.Pdf;

/// <summary>Opens a PDF and returns positioned glyphs grouped into logical lines per page.</summary>
public static class PdfTextExtractor
{
    public static IReadOnlyList<PageText> Extract(string path, double minOverlapRatio = 0.2)
    {
        using var doc = PdfDocument.Open(path);
        var pages = new List<PageText>();
        foreach (var page in doc.GetPages())
        {
            var chars = new List<PositionedChar>(page.Letters.Count);
            foreach (var l in page.Letters)
            {
                if (string.IsNullOrWhiteSpace(l.Value)) continue;
                var r = l.GlyphRectangle;
                chars.Add(new PositionedChar(
                    Text: l.Value,
                    Left: r.Left,
                    Right: r.Right,
                    Top: page.Height - r.Top,
                    Bottom: page.Height - r.Bottom,
                    FontSize: l.PointSize,
                    FontName: l.FontName));
            }

            var lines = LineBuilder.BuildLines(chars, minOverlapRatio);
            pages.Add(new PageText(page.Number, page.Width, page.Height, chars, lines));
        }
        return pages;
    }
}
