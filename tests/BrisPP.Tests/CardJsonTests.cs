using System.Text.Json;
using BrisPP.Core.Json;
using BrisPP.Core.Model;
using BrisPP.Core.Parsing;
using Xunit;

namespace BrisPP.Tests;

public class CardJsonTests
{
    [Fact]
    public void Serialize_emits_raw_and_decoded_omits_nulls_and_stringifies_enums()
    {
        var card = new RaceCard
        {
            Track = "Churchill Downs",
            Races =
            {
                new Race
                {
                    Number = 1,
                    Horses =
                    {
                        new Horse
                        {
                            ProgramNumber = 1,
                            Name = "Bhatia",
                            PastPerformances =
                            {
                                new PastPerformanceLine
                                {
                                    RawDate = "11Apr26",
                                    Surface = Surface.Turf,
                                    Distance = new Distance("1ˆ")
                                    {
                                        Surface = Surface.Turf,
                                        Display = "1 1/16m",
                                        Furlongs = 8.5m,
                                    },
                                    Start = new Call("7­"),
                                },
                            },
                        },
                    },
                },
            },
        };

        var json = CardJson.Serialize(card);

        // Raw token and decoded values both survive.
        Assert.Contains("\"Raw\": \"1\\u02C6\"", json);
        Assert.Contains("\"Furlongs\": 8.5", json);
        Assert.Contains("\"Display\": \"1 1/16m\"", json);
        // Computed call getters ride along next to the raw token.
        Assert.Contains("\"Position\": 7", json);
        // Enums serialize as names, not integers.
        Assert.Contains("\"Surface\": \"Turf\"", json);
        // Null/absent fields are dropped rather than emitted as null.
        Assert.DoesNotContain("\"Date\": null", json);
        Assert.DoesNotContain("\"Comment\"", json);
    }

    [Collection("SamplePdf")]
    public class EndToEnd
    {
        private readonly SamplePdfFixture _sample;
        public EndToEnd(SamplePdfFixture sample) => _sample = sample;

        [Fact]
        public void Full_card_serializes_to_valid_json_and_round_trips()
        {
            if (_sample.Pages is null) return; // sample PDF not present

            var card = RaceCardParser.Parse(_sample.Pages);
            var json = CardJson.Serialize(card);

            using var doc = JsonDocument.Parse(json); // throws if malformed
            var root = doc.RootElement;
            Assert.Equal("Churchill Downs", root.GetProperty("Track").GetString());
            Assert.Equal(14, root.GetProperty("Races").GetArrayLength());

            var firstPp = root.GetProperty("Races")[0]
                .GetProperty("Horses")[0]
                .GetProperty("PastPerformances")[0];
            Assert.Equal("11Apr26", firstPp.GetProperty("RawDate").GetString());
            Assert.Equal(8.5m, firstPp.GetProperty("Distance").GetProperty("Furlongs").GetDecimal());
        }
    }
}
