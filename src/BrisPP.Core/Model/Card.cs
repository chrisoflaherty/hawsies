namespace BrisPP.Core.Model;

/// <summary>The full parsed Past Performance file: one track/day card of races.</summary>
public sealed class RaceCard
{
    public string? Track { get; set; }
    public DateOnly? Date { get; set; }
    public string? Product { get; set; }
    public List<Race> Races { get; set; } = new();
}

public sealed class Race
{
    public int Number { get; set; }
    public RaceHeader Header { get; set; } = new();
    public List<Horse> Horses { get; set; } = new();
}

public sealed class RaceHeader
{
    public string? Track { get; set; }
    public DateOnly? Date { get; set; }
    public string? RaceTypeCode { get; set; }       // raw, e.g. "OC 125000n1x", "KyDerby-G1"
    public string? RaceClassification { get; set; }  // decoded/expanded description if available
    public string? Grade { get; set; }               // G1/G2/G3 if a graded stakes
    public string? StakesName { get; set; }
    public Distance? Distance { get; set; }
    public string? AgeSexConditions { get; set; }    // e.g. "3&up, F & M"
    public string? Conditions { get; set; }          // full eligibility / weight text
    public long? Purse { get; set; }
    public string? PurseText { get; set; }
    public List<string> Wagers { get; set; } = new();
    public string? Pars { get; set; }                // raw PARS string e.g. "87 90/ 85 90"
    public List<string> PostTimes { get; set; } = new();
}
