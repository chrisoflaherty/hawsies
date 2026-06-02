namespace BrisPP.Core.Model;

/// <summary>A single horse entry within a race.</summary>
public sealed class Horse
{
    public int ProgramNumber { get; set; }
    public string? ProgramEntry { get; set; }   // coupling, e.g. "1A"
    public string? Name { get; set; }
    public int? PostPosition { get; set; }
    public string? RunningStyle { get; set; }    // e.g. "S 1", "E 5", "NA 4"
    public bool Scratched { get; set; }
    public string? HorseFlag { get; set; }        // ì marker if present

    public string? Color { get; set; }            // raw, e.g. "Dkbbr"
    public string? Sex { get; set; }              // c/f/g/h/m/r
    public int? Age { get; set; }
    public string? FoalMonth { get; set; }        // e.g. "Feb"

    public long? ClaimingPrice { get; set; }       // entered-to-be-claimed tag, e.g. "$80,000"
    public string? SaleVenue { get; set; }         // auction code, e.g. "KEESEP"
    public int? SaleYear { get; set; }
    public long? SalePrice { get; set; }           // in dollars; "$450k" -> 450000
    public string? SaleRaw { get; set; }           // verbatim sale block, e.g. "KEESEP 2024 $450k"

    public decimal? PrimePower { get; set; }
    public string? PrimePowerRank { get; set; }   // e.g. "8th"
    public int? AssignedWeight { get; set; }
    public string? MedicationEquipment { get; set; }
    public string? MorningLineOdds { get; set; }

    public RaceRecord? Life { get; set; }
    public RaceRecord? CurrentYear { get; set; }
    public RaceRecord? PriorYear { get; set; }
    public RaceRecord? TrackRecord { get; set; }     // record at today's track
    public List<SurfaceRecord> SurfaceRecords { get; set; } = new();

    public Owner? Owner { get; set; }
    public Silks? Silks { get; set; }
    public string? Breeder { get; set; }
    public string? BredState { get; set; }
    public Breeding? Breeding { get; set; }

    public Trainer? Trainer { get; set; }
    public Jockey? Jockey { get; set; }

    public List<PastPerformanceLine> PastPerformances { get; set; } = new();
    public List<Workout> Workouts { get; set; } = new();
    public List<TrainerAngle> TrainerAngles { get; set; } = new();
    public JockeyTrainerStat? JtMeet { get; set; }
    public JockeyTrainerStat? JtLast365 { get; set; }

    /// <summary>Notes such as "Previously trained by ... (as of date)".</summary>
    public List<string> Notes { get; set; } = new();

    /// <summary>Verbatim identity-zone rows, preserved so the decode is auditable.</summary>
    public List<string> IdentityRaw { get; set; } = new();
}

public sealed class Owner
{
    public string? Name { get; set; }
}

public sealed class Silks
{
    public string? Description { get; set; }
}

public sealed class Breeding
{
    public string? Sire { get; set; }
    public string? SireSire { get; set; }     // sire's sire, the "(X)" after the sire
    public string? Dam { get; set; }
    public string? DamSire { get; set; }       // broodmare sire, "(X)" after the dam
    public string? StudFeeText { get; set; }   // e.g. "$75,000"
    public long? StudFee { get; set; }
}

/// <summary>Connection (trainer/jockey) with their record line.</summary>
public abstract class Connection
{
    public string? Name { get; set; }
    public int? Starts { get; set; }
    public int? Wins { get; set; }
    public int? Places { get; set; }
    public int? Shows { get; set; }
    public int? WinPercent { get; set; }
    /// <summary>Current-year stats, e.g. "2026 : (8/ 60 13%)".</summary>
    public string? YearStats { get; set; }
}

public sealed class Trainer : Connection { }
public sealed class Jockey : Connection { }
