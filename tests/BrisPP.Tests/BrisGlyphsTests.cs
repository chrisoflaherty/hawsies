using BrisPP.Core.Glyphs;
using BrisPP.Core.Model;
using Xunit;

namespace BrisPP.Tests;

public class BrisGlyphsTests
{
    [Theory]
    [InlineData("Kee¬", "Kee5")]      // race-number-on-card
    [InlineData("CD¨©", "CD12")]      // two-digit race number
    [InlineData("TP¨§", "TP10")]
    public void Decode_translates_track_race_numbers(string raw, string expected)
        => Assert.Equal(expected, BrisGlyphs.Decode(raw));

    [Theory]
    [InlineData("§", "0")]
    [InlineData("¨", "1")]
    [InlineData("©", "2")]
    [InlineData("ª", "3")]
    [InlineData("«", "4")]
    [InlineData("¬", "5")]
    [InlineData("­", "6")]
    [InlineData("®", "7")]
    [InlineData("¯", "8")]
    [InlineData("°", "9")]
    public void Decode_maps_each_superscript_digit(string glyph, string expected)
        => Assert.Equal(expected, BrisGlyphs.Decode(glyph));

    [Theory]
    [InlineData("‚", "1/4")]
    [InlineData("½", "1/2")]
    [InlineData("ƒ", "3/4")]
    [InlineData("„", "1/8")]
    [InlineData("ˆ", "1/16")]
    public void Decode_maps_each_fraction(string glyph, string expected)
        => Assert.Equal(expected, BrisGlyphs.Decode(glyph));

    [Theory]
    [InlineData(":23", 23.0)]
    [InlineData(":47¨", 47.2)]
    [InlineData("1:11©", 71.4)]
    [InlineData("1:41©", 101.4)]
    [InlineData(":59¨", 59.2)]
    public void FifthsToSeconds_decodes_times(string raw, double expected)
        => Assert.Equal((decimal)expected, BrisGlyphs.FifthsToSeconds(raw));

    [Theory]
    [InlineData("­", 6)]        // 6 lengths
    [InlineData("¬‚", 5.25)]    // 5 1/4
    [InlineData("®ƒ", 7.75)]    // 7 3/4
    [InlineData("¨©", 12)]      // 12 lengths
    public void LengthsToValue_decodes_beaten_lengths(string raw, double expected)
        => Assert.Equal((decimal)expected, BrisGlyphs.LengthsToValue(raw));

    [Theory]
    [InlineData("5¬‚", 5, 5.25)]   // 5th, 5 1/4 behind
    [InlineData("7­", 7, 6.0)]     // 7th, 6 behind
    [InlineData("10¨®", 10, 17.0)] // 10th, 17 behind
    public void ParseCall_splits_position_and_lengths(string raw, int pos, double lengths)
    {
        var (p, l) = BrisGlyphs.ParseCall(raw);
        Assert.Equal(pos, p);
        Assert.Equal((decimal)lengths, l);
    }

    [Fact]
    public void ParseCall_handles_position_without_lengths()
    {
        var (p, l) = BrisGlyphs.ParseCall("3");
        Assert.Equal(3, p);
        Assert.Null(l);
    }

    [Fact]
    public void RaceTime_display_formats_minutes_and_seconds()
    {
        Assert.Equal("1:41.4", new RaceTime("1:41©").Display);
        Assert.Equal("47.2", new RaceTime(":47¨").Display);
    }
}
