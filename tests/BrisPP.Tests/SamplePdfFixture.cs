using BrisPP.Core.Layout;
using BrisPP.Core.Pdf;
using Xunit;

namespace BrisPP.Tests;

/// <summary>
/// Opens the sample PDF once and shares the extracted pages across the
/// PDF-reading test classes. Collection membership also serializes those
/// classes, avoiding a parallel-open race in the PDF library.
/// </summary>
public sealed class SamplePdfFixture
{
    public IReadOnlyList<PageText>? Pages { get; }

    public SamplePdfFixture()
    {
        var path = SamplePath();
        if (path is not null) Pages = PdfTextExtractor.Extract(path);
    }

    private static string? SamplePath()
    {
        var env = Environment.GetEnvironmentVariable("BRIS_SAMPLE_PDF");
        var candidates = new[] { env, "/Users/chrisoflaherty/Downloads/productdownload.pdf" };
        return candidates.FirstOrDefault(p => !string.IsNullOrEmpty(p) && File.Exists(p));
    }
}

[CollectionDefinition("SamplePdf")]
public sealed class SamplePdfCollection : ICollectionFixture<SamplePdfFixture> { }
