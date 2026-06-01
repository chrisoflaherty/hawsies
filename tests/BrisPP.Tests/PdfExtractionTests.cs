using Xunit;

namespace BrisPP.Tests;

[Collection("SamplePdf")]
public class PdfExtractionTests
{
    private readonly SamplePdfFixture _sample;
    public PdfExtractionTests(SamplePdfFixture sample) => _sample = sample;

    [Fact]
    public void Extract_reads_all_pages_and_reconstructs_wide_rows()
    {
        var pages = _sample.Pages;
        if (pages is null) return; // sample PDF not present in this environment

        Assert.Equal(44, pages.Count);

        var page1 = pages[0];
        var firstLine = page1.Lines[0].Text();
        Assert.Contains("Premium Plus PP's Churchill Downs", firstLine);

        // A past-performance race row should survive intact with its glyphs.
        Assert.Contains(page1.Lines, l => l.Text().Contains("Mdn 110k") && l.Text().Contains("Kee"));
    }

    [Fact]
    public void Strip_query_separates_the_breeding_grid_columns()
    {
        var pages = _sample.Pages;
        if (pages is null) return; // sample PDF not present in this environment

        var page1 = pages[0];

        // Breeding column for the first horse: Sire and Dam are stacked at
        // half-line spacing and must land on separate rows, not interleaved.
        var rows = page1.RowsInStrip(198, 378, 128, 150)
            .Select(r => r.Text()).ToList();

        Assert.Contains(rows, r => r.Contains("Sire") && r.Contains("Medaglia"));
        Assert.Contains(rows, r => r.Contains("Dam") && r.Contains("Langtry"));
        // Critically, no row mixes both labels (the old interleave bug).
        Assert.DoesNotContain(rows, r => r.Contains("Sire") && r.Contains("Dam"));
    }
}
