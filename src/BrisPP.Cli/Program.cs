using System.Globalization;
using BrisPP.Core.Json;
using BrisPP.Core.Parsing;
using BrisPP.Core.Pdf;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage:");
    Console.Error.WriteLine("  json  <pdf> [outFile]                    parse the card and write structured JSON");
    Console.Error.WriteLine("  parse <pdf> [raceNo] [horseNo]           parse and print a human-readable summary");
    Console.Error.WriteLine("  dump  <pdf> [pageNumber]                 print logical lines for a page");
    Console.Error.WriteLine("  chars <pdf> <pageNumber> <topMin> <topMax>  print glyph X positions in a band");
    Console.Error.WriteLine("  strip <pdf> <pageNumber> <xMin> <xMax> <yMin> <yMax>  print a column strip");
    return 1;
}

var cmd = args[0];
var pdf = args[1];
var tol = Environment.GetEnvironmentVariable("BRIS_TOL") is { } t
    ? double.Parse(t, CultureInfo.InvariantCulture) : 0.2;
var pages = PdfTextExtractor.Extract(pdf, tol);

switch (cmd)
{
    case "dump":
    {
        int pageNo = args.Length > 2 ? int.Parse(args[2]) : 1;
        var page = pages.First(p => p.Number == pageNo);
        Console.WriteLine($"# page {page.Number}  {page.Width}x{page.Height}  lines={page.Lines.Count}");
        foreach (var line in page.Lines)
            Console.WriteLine($"{line.Baseline,7:0.0} | {line.Text()}");
        break;
    }
    case "chars":
    {
        int pageNo = int.Parse(args[2]);
        double topMin = double.Parse(args[3], CultureInfo.InvariantCulture);
        double topMax = double.Parse(args[4], CultureInfo.InvariantCulture);
        var page = pages.First(p => p.Number == pageNo);
        foreach (var line in page.Lines)
        {
            if (line.Baseline < topMin || line.Baseline > topMax) continue;
            Console.WriteLine($"--- baseline {line.Baseline:0.0} ---");
            foreach (var c in line.Chars)
                Console.WriteLine($"  x={c.Left,6:0.0} cx={c.CenterX,6:0.0} sz={c.FontSize,4:0.0} {Display(c.Text)}");
        }
        break;
    }
    case "strip":
    {
        int pageNo = int.Parse(args[2]);
        double xMin = double.Parse(args[3], CultureInfo.InvariantCulture);
        double xMax = double.Parse(args[4], CultureInfo.InvariantCulture);
        double yMin = double.Parse(args[5], CultureInfo.InvariantCulture);
        double yMax = double.Parse(args[6], CultureInfo.InvariantCulture);
        var page = pages.First(p => p.Number == pageNo);
        foreach (var row in page.RowsInStrip(xMin, xMax, yMin, yMax))
            Console.WriteLine($"{row.Baseline,7:0.0} | {row.Text()}");
        break;
    }
    case "json":
    {
        var card = RaceCardParser.Parse(pages);
        if (args.Length > 2)
        {
            CardJson.Write(card, args[2]);
            var horses = card.Races.Sum(r => r.Horses.Count);
            Console.Error.WriteLine($"wrote {args[2]}: {card.Races.Count} races, {horses} horses");
        }
        else Console.WriteLine(CardJson.Serialize(card));
        break;
    }
    case "parse":
    {
        int? onlyRace = args.Length > 2 ? int.Parse(args[2]) : null;
        int? onlyHorse = args.Length > 3 ? int.Parse(args[3]) : null;
        var card = RaceCardParser.Parse(pages);
        Console.WriteLine($"{card.Product} | {card.Track} | {card.Date}");
        Console.WriteLine($"races={card.Races.Count}");
        foreach (var race in card.Races)
        {
            if (onlyRace is { } rn && race.Number != rn) continue;
            var h = race.Header;
            Console.WriteLine($"\n== Race {race.Number}: {h.RaceTypeCode}  {h.Distance?.Display}  " +
                $"{h.AgeSexConditions}  purse={h.PurseText}  grade={h.Grade}  horses={race.Horses.Count}");
            foreach (var horse in race.Horses)
            {
                if (onlyHorse is { } hn && horse.ProgramNumber != hn) continue;
                Console.WriteLine($"  #{horse.ProgramNumber} {horse.Name} ({horse.RunningStyle})  " +
                    $"{horse.Color} {horse.Sex} {horse.Age} ({horse.FoalMonth})  PP={horse.PrimePower} {horse.PrimePowerRank}");
                Console.WriteLine($"      Life={Fmt(horse.Life)}  {horse.CurrentYear?.Label}={Fmt(horse.CurrentYear)}");
                Console.WriteLine($"      Sire={horse.Breeding?.Sire} ({horse.Breeding?.SireSire}) Dam={horse.Breeding?.Dam} ({horse.Breeding?.DamSire})");
                Console.WriteLine($"      Own={horse.Owner?.Name}  Brdr={horse.Breeder} [{horse.BredState}]");
                Console.WriteLine($"      Trnr={horse.Trainer?.Name} {horse.Trainer?.Wins}/{horse.Trainer?.Starts}  Jky={horse.Jockey?.Name} {horse.Jockey?.WinPercent}%  ML={horse.MorningLineOdds}");

                foreach (var pp in horse.PastPerformances)
                {
                    var fins = string.Join(", ", pp.TopFinishers.Select(f =>
                        f.Margin is null ? f.Name : $"{f.Name}[{f.Margin.Display}]"));
                    Console.WriteLine($"      PP {pp.RawDate} {pp.Track}{(pp.TrackRaceNumber is { } r ? $"#{r}" : "")} " +
                        $"{pp.Distance?.Display} {pp.Surface} {pp.TrackCondition}  {pp.RaceType}");
                    Console.WriteLine($"         E1={pp.Figures?.E1} E2={pp.Figures?.E2} LP={pp.Figures?.LatePace} " +
                        $"SPD={pp.Speed} PP={pp.PostPosition}  calls[{pp.Start?.Display} {pp.FirstCall?.Display} {pp.SecondCall?.Display} {pp.Stretch?.Display} {pp.Finish?.Display}]");
                    Console.WriteLine($"         Jky={pp.Jockey} wt={pp.CarriedWeight} med={pp.MedicationEquipment} " +
                        $"odds={pp.Odds}{(pp.Favorite ? "*" : "")} field={pp.FieldSize}");
                    Console.WriteLine($"         fin: {fins}");
                    if (pp.Comment is not null) Console.WriteLine($"         \"{pp.Comment}\"");
                }
                foreach (var w in horse.Workouts)
                    Console.WriteLine($"      WK {(w.Bullet ? "* " : "")}{w.RawDate} {w.Track} {w.Distance?.Display} " +
                        $"{w.Condition} {w.Time?.Display} {w.Designation} {w.Rank}/{w.OutOf}");
                foreach (var a in horse.TrainerAngles)
                    Console.WriteLine($"      ANG {a.Category}: {a.Starts} starts {a.WinPercent}% roi={a.Roi}");
                if (horse.JtMeet is { } jm) Console.WriteLine($"      J/T Meet: {jm.Starts} {jm.WinRate} {jm.Roi}");
                if (horse.JtLast365 is { } jl) Console.WriteLine($"      J/T L365: {jl.Starts} {jl.WinRate} {jl.Roi}");
                foreach (var n in horse.Notes) Console.WriteLine($"      NOTE {n}");
            }
        }
        break;
    }
    default:
        Console.Error.WriteLine($"Unknown command: {cmd}");
        return 1;
}

return 0;

static string Display(string s)
{
    if (s.Length == 1 && (s[0] < 32 || s[0] == 127))
        return $"U+{(int)s[0]:X4}";
    return s;
}

static string Fmt(BrisPP.Core.Model.RaceRecord? r) =>
    r is null ? "-" : $"{r.Starts} {r.Wins}-{r.Places}-{r.Shows} ${r.Earnings} ({r.BestSpeed})";
