namespace BrisPP.Core.Model;

/// <summary>One past race line for a horse.</summary>
public sealed class PastPerformanceLine
{
    public DateOnly? Date { get; set; }
    public string? RawDate { get; set; }
    public string? Track { get; set; }
    public int? TrackRaceNumber { get; set; }
    public string? TrackCondition { get; set; }    // ft/fm/gd/sy/sly...
    public Surface Surface { get; set; } = Surface.Unknown;
    public Distance? Distance { get; set; }
    public string? RaceType { get; set; }           // e.g. "Mdn 110k", "MC50000"
    public List<RaceTime> Fractions { get; set; } = new();  // 2f/4f/6f/final splits

    public PaceFigures? Figures { get; set; }
    public int? PaceAdjust1c { get; set; }          // "1c" pace adjustment
    public int? PaceAdjust2c { get; set; }          // "2c"
    public int? Speed { get; set; }                 // SPD (BRIS speed figure)
    public int? PostPosition { get; set; }          // PP column

    public Call? Start { get; set; }                // ST
    public Call? FirstCall { get; set; }            // 1C
    public Call? SecondCall { get; set; }           // 2C
    public Call? Stretch { get; set; }              // Str
    public Call? Finish { get; set; }               // Fin

    public string? Jockey { get; set; }
    public int? CarriedWeight { get; set; }
    public string? MedicationEquipment { get; set; }
    public decimal? Odds { get; set; }
    public bool Favorite { get; set; }
    public List<Finisher> TopFinishers { get; set; } = new();
    public string? Comment { get; set; }
    public int? FieldSize { get; set; }
    public string? RaceFlag { get; set; }           // ¦ marker if present
}

public sealed record Finisher(string Name, Margin? Margin);

/// <summary>A published workout line.</summary>
public sealed class Workout
{
    public DateOnly? Date { get; set; }
    public string? RawDate { get; set; }
    public string? Track { get; set; }
    public Distance? Distance { get; set; }
    public string? Condition { get; set; }          // ft/gd/sly...
    public RaceTime? Time { get; set; }
    public string? Designation { get; set; }        // B (breezing), H (handily), Bg (gate)...
    public bool Bullet { get; set; }                // × best-of marker
    public int? Rank { get; set; }                  // rank among works that day
    public int? OutOf { get; set; }                 // total works at the distance
}

/// <summary>A trainer angle/statistic, e.g. "Maiden Sp Wt(126 15% +0.11)".</summary>
public sealed class TrainerAngle
{
    public string? Category { get; set; }
    public int? Starts { get; set; }
    public int? WinPercent { get; set; }
    public decimal? Roi { get; set; }               // return on investment $ figure
    public string? Raw { get; set; }                // original text; numbers can scatter
}

/// <summary>Jockey/Trainer combo stat, e.g. "J/T L365: ( 38 .24 +$0.31 )".</summary>
public sealed class JockeyTrainerStat
{
    public int? Starts { get; set; }
    public decimal? WinRate { get; set; }
    public decimal? Roi { get; set; }
    public string? Raw { get; set; }                // original text; numbers can scatter
}
