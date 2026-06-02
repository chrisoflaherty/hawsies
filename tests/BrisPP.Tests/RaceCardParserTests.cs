using BrisPP.Core.Model;
using BrisPP.Core.Parsing;
using Xunit;

namespace BrisPP.Tests;

[Collection("SamplePdf")]
public class RaceCardParserTests
{
    private readonly SamplePdfFixture _sample;
    public RaceCardParserTests(SamplePdfFixture sample) => _sample = sample;

    [Theory]
    [InlineData(
        "Premium Plus PP's Churchill Downs CD-G1 7 Furlongs 4&up Saturday, May02, 2026 Race 10",
        10, "Churchill Downs", "CD-G1", "7 Furlongs", 7, "4&up")]
    [InlineData(
        "Premium Plus PP's Churchill Downs OC 80000n2x 1 Mile 3&up Saturday, May02, 2026 Race 3",
        3, "Churchill Downs", "OC 80000n2x", "1 Mile", 8, "3&up")]
    public void ReadTopLine_splits_track_type_distance_and_number(
        string line, int number, string track, string type, string distDisplay,
        int furlongs, string ageSex)
    {
        var top = HeaderParser.ReadTopLine(line);
        Assert.NotNull(top);
        Assert.Equal(number, top!.Number);
        Assert.Equal(track, top.Track);
        Assert.Equal(type, top.RaceTypeCode);
        Assert.Equal(distDisplay, top.Distance!.Display);
        Assert.Equal(furlongs, top.Distance!.Furlongs);
        Assert.Equal(ageSex, top.AgeSex);
    }

    [Fact]
    public void ReadTopLine_returns_null_for_trailer_pages()
    {
        Assert.Null(HeaderParser.ReadTopLine("Premium Plus PP's Churchill Downs"));
    }

    [Fact]
    public void Parse_splits_the_card_into_fourteen_races()
    {
        if (_sample.Pages is null) return; // sample PDF not present

        var card = RaceCardParser.Parse(_sample.Pages);

        Assert.Equal("Churchill Downs", card.Track);
        Assert.Equal(new DateOnly(2026, 5, 2), card.Date);
        Assert.Equal(14, card.Races.Count);
        Assert.Equal(Enumerable.Range(1, 14), card.Races.Select(r => r.Number));

        // The Kentucky Derby is race 12 with a full 20+ horse field.
        var derby = card.Races[11];
        Assert.Equal("G1", derby.Header.Grade);
        Assert.True(derby.Horses.Count >= 20);
    }

    [Fact]
    public void Parse_reads_horse_identity_for_the_first_race()
    {
        if (_sample.Pages is null) return; // sample PDF not present

        var race1 = RaceCardParser.Parse(_sample.Pages).Races[0];
        Assert.Equal(10, race1.Horses.Count);

        var one = race1.Horses[0];
        Assert.Equal(1, one.ProgramNumber);
        Assert.Equal("Bhatia", one.Name);
        Assert.Equal(3, one.Age);
        Assert.Equal("Feb", one.FoalMonth);
        Assert.Equal(118.4m, one.PrimePower);
        Assert.Equal("Medaglia d Oro", one.Breeding!.Sire);
        Assert.Equal("Langtry", one.Breeding!.Dam);
        Assert.Equal("Walmac Farm", one.Owner!.Name);
        Assert.Equal("KY", one.BredState);

        // Life record survives the comma/dash glyph scatter.
        Assert.Equal(5, one.Life!.Starts);
        Assert.Equal(18892, one.Life!.Earnings);

        // Multi-word bold names keep their internal space.
        Assert.Equal("Silent Way", race1.Horses[2].Name);
    }

    [Fact]
    public void Parse_reads_identity_across_layout_variants()
    {
        if (_sample.Pages is null) return; // sample PDF not present

        var card = RaceCardParser.Parse(_sample.Pages);

        // Sale-history horses show "Ch. g. 3 OBSAPR 2025" with no foal-month
        // parens, where the old parser silently dropped color/sex/age.
        var woods = card.Races[1].Horses[0];
        Assert.Equal("Out of the Woods", woods.Name);
        Assert.Equal("Ch", woods.Color);
        Assert.Equal("g", woods.Sex);
        Assert.Equal(3, woods.Age);
        Assert.Null(woods.FoalMonth);

        // A suffixed trainer name ("Joseph, Jr. Saffie A") must survive the comma.
        var haulin = card.Races[3].Horses.Single(h => h.ProgramNumber == 2);
        Assert.StartsWith("Jose", haulin.Trainer!.Name);
        Assert.Contains("Jr.", haulin.Trainer!.Name);

        // Prime Power rank can be bare ("157.0 5th") instead of parenthesized.
        var gold = card.Races[10].Horses.Single(h => h.ProgramNumber == 7);
        Assert.Equal(157.0m, gold.PrimePower);
        Assert.Equal("5th", gold.PrimePowerRank);
    }

    [Fact]
    public void Parse_reads_sale_history_and_claiming_price()
    {
        if (_sample.Pages is null) return; // sample PDF not present

        var card = RaceCardParser.Parse(_sample.Pages);

        // Sale tag in place of the foal-month paren: "Ch. g. 3 OBSAPR 2025 $325k".
        var woods = card.Races[1].Horses[0];
        Assert.Equal("Out of the Woods", woods.Name);
        Assert.Equal("OBSAPR", woods.SaleVenue);
        Assert.Equal(2025, woods.SaleYear);
        Assert.Equal(325_000, woods.SalePrice);   // "$325k" scales to whole dollars
        Assert.Equal("OBSAPR 2025 $325k", woods.SaleRaw);
        Assert.Null(woods.ClaimingPrice);
        Assert.Null(woods.FoalMonth);

        // Claiming-price prefix and a sale tag coexist on the same row:
        // "$80,000 Dkbbr. g. 6 KEESEP 2021 $360k".
        var arro = card.Races[2].Horses.Single(h => h.ProgramNumber == 1);
        Assert.Equal(80_000, arro.ClaimingPrice);
        Assert.Equal("KEESEP", arro.SaleVenue);
        Assert.Equal(2021, arro.SaleYear);
        Assert.Equal(360_000, arro.SalePrice);

        // A fractional "k" price keeps its precision: "$190.4k" -> 190,400.
        var portfolio = card.Races[6].Horses.Single(h => h.ProgramNumber == 3);
        Assert.Equal(190_400, portfolio.SalePrice);
        Assert.Equal("TATOCT", portfolio.SaleVenue);

        // A foal-month horse (no sale history) leaves the sale fields untouched.
        var bhatia = card.Races[0].Horses[0];
        Assert.Equal("Feb", bhatia.FoalMonth);
        Assert.Null(bhatia.SaleVenue);
        Assert.Null(bhatia.SalePrice);
        Assert.Null(bhatia.ClaimingPrice);
    }

    [Fact]
    public void Parse_leaves_prime_power_null_only_for_the_shippers_absent_in_source()
    {
        if (_sample.Pages is null) return; // sample PDF not present

        var card = RaceCardParser.Parse(_sample.Pages);
        var missing = card.Races
            .SelectMany(r => r.Horses.Select(h => (r.Number, h.ProgramNumber, h.Name, h.PrimePower)))
            .Where(x => x.PrimePower is null)
            .Select(x => (x.Number, x.ProgramNumber))
            .ToHashSet();

        // These six foreign/JRA shippers carry no "Prime Power" figure in the
        // BRIS PDF at all (confirmed at the glyph level — not clipped, not
        // scattered), so the parser leaves PrimePower null rather than invent one.
        // Every other horse must resolve it.
        var expected = new HashSet<(int, int)>
        {
            (10, 8), (10, 10), (11, 10), (12, 7), (12, 10), (12, 17),
        };
        Assert.Equal(expected, missing);
    }

    [Fact]
    public void Parse_populates_core_connections_for_every_horse()
    {
        if (_sample.Pages is null) return; // sample PDF not present

        var horses = RaceCardParser.Parse(_sample.Pages).Races
            .SelectMany(r => r.Horses).ToList();
        Assert.Equal(161, horses.Count);

        // The deliverable's contract: every horse resolves its identity and
        // connections and carries at least one past-performance line.
        Assert.All(horses, h =>
        {
            Assert.False(string.IsNullOrEmpty(h.Name));
            Assert.NotNull(h.Color);
            Assert.NotNull(h.Age);
            Assert.NotNull(h.Owner);
            Assert.NotNull(h.Trainer);
            Assert.NotNull(h.Jockey);
            Assert.NotNull(h.Breeding);
            Assert.NotEmpty(h.PastPerformances);
        });
    }

    [Fact]
    public void Parse_reads_past_performance_lines_for_the_first_horse()
    {
        if (_sample.Pages is null) return; // sample PDF not present

        var one = RaceCardParser.Parse(_sample.Pages).Races[0].Horses[0];
        Assert.Equal(5, one.PastPerformances.Count);

        var top = one.PastPerformances[0];
        Assert.Equal("11Apr26", top.RawDate);
        Assert.Equal("Kee", top.Track);
        Assert.Equal(5, top.TrackRaceNumber);
        Assert.Equal(Surface.Turf, top.Surface);
        Assert.Equal("1 1/16m", top.Distance!.Display);
        Assert.Equal(8.5m, top.Distance!.Furlongs);
        Assert.Equal("fm", top.TrackCondition);
        Assert.Equal("Mdn 110k", top.RaceType);

        Assert.Equal(79, top.Figures!.E1);
        Assert.Equal(88, top.Figures!.E2);
        Assert.Equal(67, top.Figures!.LatePace);
        Assert.Equal(76, top.Speed);
        Assert.Equal(5, top.PostPosition);

        Assert.Equal("OrtizIJ", top.Jockey);
        Assert.Equal(120, top.CarriedWeight);
        Assert.Equal(14.42m, top.Odds);
        Assert.False(top.Favorite);
        Assert.Equal(11, top.FieldSize);
        Assert.Equal("MgclFctr", top.TopFinishers[0].Name);
        Assert.Equal("Ins;closr;lackkckbtw", top.Comment);

        // A bottom dirt sprint marked "about" keeps its furlong unit.
        var sprint = one.PastPerformances[4];
        Assert.Equal("*7f", sprint.Distance!.Display);
        Assert.Equal(7m, sprint.Distance!.Furlongs);
        Assert.Equal(Surface.Dirt, sprint.Surface);
    }

    [Fact]
    public void Parse_reads_workouts_angles_and_combo_stats_for_the_first_horse()
    {
        if (_sample.Pages is null) return; // sample PDF not present

        var one = RaceCardParser.Parse(_sample.Pages).Races[0].Horses[0];

        Assert.Equal(12, one.Workouts.Count);
        var w0 = one.Workouts[0];
        Assert.Equal("04Apr", w0.RawDate);
        Assert.Equal("TP", w0.Track);
        Assert.Equal("4f", w0.Distance!.Display);
        Assert.Equal("ft", w0.Condition);
        Assert.Equal("B", w0.Designation);
        Assert.Equal(50, w0.Rank);
        Assert.Equal(78, w0.OutOf);

        // The rank total must not bleed into the next workout's date digit.
        Assert.Equal(128, one.Workouts[5].OutOf);

        // Scatter-split start counts ("1 26") resolve to the whole number.
        var maiden = one.TrainerAngles.Single(a => a.Category == "Maiden Sp Wt");
        Assert.Equal(126, maiden.Starts);
        Assert.Equal(15, maiden.WinPercent);
        Assert.Equal(0.11m, maiden.Roi);

        Assert.NotNull(one.JtLast365);
        Assert.Equal(38, one.JtLast365!.Starts);
        Assert.Equal(0.24m, one.JtLast365!.WinRate);
        Assert.Equal(0.31m, one.JtLast365!.Roi);
    }
}
