using System.Text.RegularExpressions;
using BrisPP.Core.Glyphs;
using BrisPP.Core.Layout;
using BrisPP.Core.Model;
using BrisPP.Core.Pdf;

namespace BrisPP.Core.Parsing;

/// <summary>
/// Extracts horse identity blocks from a page. Each block is anchored on its
/// "DATE TRK DIST RACETYPE..." past-performance header; the identity zone sits
/// in the ~42pt band above it. The program number and bold name are isolated by
/// font size; the remaining columnar fields are read from baseline-clustered
/// rows, which fall in clean left-to-right order within the zone.
/// </summary>
public static partial class HorseParser
{
    private const double ZoneHeight = 42;
    private const double BoldMin = 9.5;
    private const double BoldMax = 14;

    public static List<Horse> ParsePage(PageText page)
    {
        var horses = new List<Horse>();
        var headers = page.Lines
            .Where(IsPpHeader)
            .Select(l => l.Baseline)
            .OrderBy(b => b)
            .ToList();

        for (int i = 0; i < headers.Count; i++)
        {
            var baseline = headers[i];
            var horse = ParseIdentity(page, baseline);
            if (horse is null) continue;

            // Running lines fill the gap between this PP header and the next
            // horse's identity zone (or the page bottom for the last block).
            double blockBottom = i + 1 < headers.Count
                ? headers[i + 1] - ZoneHeight - 0.5
                : page.Height;
            RunningLineParser.Populate(page, baseline, blockBottom, horse);
            horses.Add(horse);
        }
        return horses;
    }

    private static bool IsPpHeader(TextLine l)
    {
        var t = l.Text();
        return t.StartsWith("DATE TRK DIST", StringComparison.OrdinalIgnoreCase)
            || t.Contains("RACETYPE E1 E2", StringComparison.OrdinalIgnoreCase);
    }

    private static Horse? ParseIdentity(PageText page, double headerBaseline)
    {
        double top = headerBaseline - ZoneHeight;
        double bottom = headerBaseline - 1.5;

        var zoneChars = page.Chars
            .Where(c => c.Baseline > top && c.Baseline < bottom)
            .ToList();
        if (zoneChars.Count == 0) return null;

        var horse = new Horse();
        ReadProgram(zoneChars, horse);
        ReadName(zoneChars, horse);

        var rows = page.RowsInStrip(0, page.Width, top, bottom, 2.5)
            .Select(r => r.Text().Trim())
            .Where(t => t.Length > 0)
            .ToList();
        horse.IdentityRaw = rows;
        foreach (var row in rows) ApplyRow(row, horse);

        return horse.ProgramNumber > 0 || horse.Name is not null ? horse : null;
    }

    private static void ReadProgram(List<PositionedChar> zoneChars, Horse horse)
    {
        var prog = zoneChars.Where(c => c.FontSize >= BoldMax)
            .OrderBy(c => c.Left).ToList();
        if (prog.Count == 0) return;
        var text = string.Concat(prog.Select(c => c.Text)).Trim();
        horse.ProgramEntry = text;
        var digits = new string(text.Where(char.IsDigit).ToArray());
        if (int.TryParse(digits, out var pn)) horse.ProgramNumber = pn;
    }

    private static void ReadName(List<PositionedChar> zoneChars, Horse horse)
    {
        var bold = zoneChars.Where(c => c.FontSize >= BoldMin && c.FontSize < BoldMax)
            .OrderBy(c => c.Left).ToList();
        if (bold.Count == 0) return;

        var text = new TextLine(bold.Max(c => c.Baseline), bold).Text();
        if (text.Contains(BrisGlyphs.HorseFlagMarker))
        {
            horse.HorseFlag = BrisGlyphs.HorseFlagMarker.ToString();
            text = text.Replace(BrisGlyphs.HorseFlagMarker.ToString(), "");
        }
        text = BrisGlyphs.Decode(text).Trim();

        var paren = text.IndexOf('(');
        if (paren >= 0)
        {
            horse.Name = text[..paren].Trim();
            var close = text.IndexOf(')', paren);
            var style = (close > paren ? text[(paren + 1)..close] : text[(paren + 1)..]).Trim();
            horse.RunningStyle = NormalizeStyle(style);
        }
        else horse.Name = text.Trim();
    }

    private static string NormalizeStyle(string s)
    {
        var m = StyleRx().Match(s);
        return m.Success ? $"{m.Groups[1].Value} {m.Groups[2].Value}" : s;
    }

    private static void ApplyRow(string row, Horse horse)
    {
        if (horse.Color is null)
        {
            var c = ColorRx().Match(row);
            if (c.Success)
            {
                horse.Color = c.Groups[1].Value;
                if (c.Groups[2].Success) horse.Sex = c.Groups[2].Value;
                horse.Age = int.Parse(c.Groups[3].Value);
                // Foal month only shows for horses without sale history; others
                // carry a sale tag ("OBSAPR 2025 $325k") in its place.
                if (c.Groups[4].Success) horse.FoalMonth = c.Groups[4].Value;
                ApplyIdentityPrices(row, horse);
            }
        }

        if (horse.PrimePower is null)
        {
            var pp = PrimePowerRx().Match(row);
            if (pp.Success && decimal.TryParse(pp.Groups[1].Value, out var v))
            {
                horse.PrimePower = v;
                horse.PrimePowerRank = pp.Groups[2].Value;
            }
        }

        if (horse.Life is null)
        {
            var life = LifeRx().Match(row);
            if (life.Success)
                horse.Life = ParseHelpers.Record(life.Groups[1].Value) is { } r
                    ? r with { Label = "Life" } : null;
        }

        // Skip connection rows (Trnr/Jockey), whose "2026 : .." year-stats are
        // not win-place-show year records.
        var yearMatches = row.Contains('%')
            ? Enumerable.Empty<Match>()
            : YearRx().Matches(row).Cast<Match>();
        foreach (var y in yearMatches)
        {
            var year = int.Parse(y.Groups[1].Value);
            var rec = ParseHelpers.Record(row[(y.Index + y.Length)..]);
            if (rec is null) continue;
            rec = rec with { Label = year.ToString() };
            if (horse.CurrentYear is null || year > YearOf(horse.CurrentYear))
            {
                horse.PriorYear ??= horse.CurrentYear;
                horse.CurrentYear = rec;
            }
            else if (horse.PriorYear is null || year > YearOf(horse.PriorYear))
                horse.PriorYear = rec;
        }

        var sire = SireRx().Match(row);
        if (sire.Success && horse.Breeding?.Sire is null)
            ApplySire(sire.Groups[1].Value, horse);

        var own = OwnRx().Match(row);
        if (own.Success && horse.Owner is null)
            horse.Owner = new Owner { Name = own.Groups[1].Value.Trim() };

        var dam = DamRx().Match(row);
        if (dam.Success && horse.Breeding?.Dam is null)
            ApplyDam(dam.Groups[1].Value, horse);

        var brdr = BrdrRx().Match(row);
        if (brdr.Success && horse.Breeder is null)
        {
            var body = brdr.Groups[1].Value.Trim();
            var state = BredStateRx().Match(body);
            if (state.Success)
            {
                horse.BredState = state.Groups[1].Value;
                body = body[..state.Index].TrimEnd(' ', '-');
            }
            horse.Breeder = body;
        }

        var trnr = TrnrRx().Match(row);
        if (trnr.Success && horse.Trainer is null)
            horse.Trainer = BuildConnection<Trainer>(trnr);

        if (horse.Jockey is null)
        {
            var jock = JockeyRx().Match(row);
            if (jock.Success) horse.Jockey = BuildConnection<Jockey>(jock);
        }

        if (horse.MorningLineOdds is null)
        {
            var ml = MorningLineRx().Match(row);
            if (ml.Success) horse.MorningLineOdds = ml.Groups[1].Value;
        }
    }

    // Both prices live on the color row. The claiming price prefixes it when the
    // horse is entered to be claimed; the sale block ("KEESEP 2024 $450k") sits
    // after the age in place of the foal-month paren.
    private static void ApplyIdentityPrices(string row, Horse horse)
    {
        var claim = ClaimingRx().Match(row);
        if (claim.Success) horse.ClaimingPrice = ParseHelpers.Money(claim.Groups[1].Value);

        var sale = SaleRx().Match(row);
        if (sale.Success)
        {
            horse.SaleRaw = sale.Value.Trim();
            horse.SaleVenue = sale.Groups[1].Value;
            horse.SaleYear = int.Parse(sale.Groups[2].Value);
            horse.SalePrice = SalePriceDollars(sale.Groups[3].Value);
        }
    }

    // Sale prices are quoted in thousands with a "k" suffix ("$450k", "$190.4k");
    // scale to whole dollars. A bare amount (no "k") is taken at face value.
    private static long? SalePriceDollars(string token)
    {
        if (ParseHelpers.Decimal(token) is not { } value) return null;
        var scale = token.Contains('k', StringComparison.OrdinalIgnoreCase) ? 1000 : 1;
        return (long)Math.Round(value * scale);
    }

    private static T BuildConnection<T>(Match m) where T : Connection, new()
    {
        var c = new T { Name = m.Groups[1].Value.Trim() };
        if (int.TryParse(m.Groups[2].Value, out var s)) c.Starts = s;
        if (int.TryParse(m.Groups[3].Value, out var w)) c.Wins = w;
        if (int.TryParse(m.Groups[4].Value, out var p)) c.Places = p;
        if (int.TryParse(m.Groups[5].Value, out var sh)) c.Shows = sh;
        if (int.TryParse(m.Groups[6].Value, out var pct)) c.WinPercent = pct;
        if (m.Groups[7].Success) c.YearStats = m.Groups[7].Value.Trim();
        return c;
    }

    private static void ApplySire(string text, Horse horse)
    {
        horse.Breeding ??= new Breeding();
        var (name, paren, fee) = SplitBreeding(text);
        horse.Breeding.Sire = name;
        horse.Breeding.SireSire = paren;
        if (fee is not null)
        {
            horse.Breeding.StudFeeText = fee;
            horse.Breeding.StudFee = ParseHelpers.Money(fee);
        }
    }

    private static void ApplyDam(string text, Horse horse)
    {
        horse.Breeding ??= new Breeding();
        var (name, paren, _) = SplitBreeding(text);
        horse.Breeding.Dam = name;
        horse.Breeding.DamSire = paren;
    }

    private static (string Name, string? Paren, string? Fee) SplitBreeding(string text)
    {
        text = text.Trim();
        string? fee = null;
        var dollar = text.IndexOf('$');
        if (dollar >= 0)
        {
            fee = text[dollar..].Trim();
            text = text[..dollar].TrimEnd();
        }
        string? paren = null;
        var open = text.IndexOf('(');
        if (open >= 0)
        {
            var close = text.LastIndexOf(')');
            if (close > open) paren = text[(open + 1)..close].Trim();
            text = text[..open].TrimEnd();
        }
        return (text.Trim(), paren, fee);
    }

    private static int YearOf(RaceRecord r) => int.TryParse(r.Label, out var y) ? y : 0;

    [GeneratedRegex(@"^([A-Za-z]+)\s*(\d+)$")]
    private static partial Regex StyleRx();

    // Color can carry a slash ("Gr/ro"); the row may be prefixed by a claiming
    // or sale price ("$80,000 Dkbbr. g. 6"), so the anchor allows a leading gap.
    // Sex is optional because a descender ("g") can scatter onto the name row,
    // leaving "Ch. . 8" — we still want the color and age.
    [GeneratedRegex(@"(?:^|\s)([A-Za-z][A-Za-z/]*)\.\s*([cfghmrCFGHMR])?\.\s*(\d+)\b(?:\s*\(([A-Za-z]{3})\))?")]
    private static partial Regex ColorRx();

    // A leading claiming price ("$80,000 Dkbbr. g. 6 ...") on the color row.
    [GeneratedRegex(@"^\s*\$\s*([\d,]+)\b")]
    private static partial Regex ClaimingRx();

    // Sale block "<AUCTION> <year> $<price>[k]" after the age. Auction codes run
    // 5-6 caps in this card; allow 3-7 for safety. The year+$ anchor keeps it
    // from matching the color token or an all-caps name fragment.
    [GeneratedRegex(@"\b([A-Z]{3,7})\s+(20\d{2})\s+(\$[\d.,]+[kK]?)")]
    private static partial Regex SaleRx();

    // The rank ordinal ("6th") is parenthesized for most horses but bare for
    // some ("157.0 5th"), so the parentheses are optional.
    [GeneratedRegex(@"Prime\s*Power:\s*([\d.]+)\s*\(?(\d+(?:st|nd|rd|th))\)?")]
    private static partial Regex PrimePowerRx();

    [GeneratedRegex(@"Life:\s*([\d\s$,\-]+?)(?=\s+[A-Za-z]|$)")]
    private static partial Regex LifeRx();

    [GeneratedRegex(@"\b(20\d{2})\b")]
    private static partial Regex YearRx();

    [GeneratedRegex(@"Sire\s*:\s*(.+)$")]
    private static partial Regex SireRx();

    [GeneratedRegex(@"Own:\s*(.+?)(?:\s+Dam:|$)")]
    private static partial Regex OwnRx();

    [GeneratedRegex(@"Dam:\s*(.+)$")]
    private static partial Regex DamRx();

    [GeneratedRegex(@"Brdr:\s*(.+)$")]
    private static partial Regex BrdrRx();

    [GeneratedRegex(@"\b([A-Z]{2})\b(?!.*\b[A-Z]{2}\b)")]
    private static partial Regex BredStateRx();

    // The name class includes a comma so suffixed names ("Joseph, Jr. Saffie A")
    // are captured whole instead of truncating at the comma.
    [GeneratedRegex(@"Trnr:\s*([A-Za-z .,'\-]+?)\s+(\d+)\s+(\d+)\s*-\s*(\d+)\s*-\s*(\d+)\s+(\d+)%(?:\s*(20\d{2}\s*:.*?%))?")]
    private static partial Regex TrnrRx();

    [GeneratedRegex(@"^([A-Z][A-Z][A-Z .,'\-]+?)\s+(\d+)\s+(\d+)\s*-\s*(\d+)\s*-\s*(\d+)\s+(\d+)%(?:\s*(20\d{2}\s*:.*?%))?")]
    private static partial Regex JockeyRx();

    [GeneratedRegex(@"^\s*(\d{1,3}/\d{1,2})\b")]
    private static partial Regex MorningLineRx();
}
