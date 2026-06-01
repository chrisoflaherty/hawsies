using BrisPP.Core.Glyphs;
using BrisPP.Core.Layout;
using BrisPP.Core.Model;

namespace BrisPP.Core.Parsing;

/// <summary>
/// Top-level parser: groups the extracted pages into races (a race spans its
/// first page plus continuation pages sharing the same race number) and fills
/// each race with its header and horse identity blocks.
/// </summary>
public static class RaceCardParser
{
    public static RaceCard Parse(IReadOnlyList<PageText> pages)
    {
        var card = new RaceCard();
        var groups = new List<(int Number, List<PageText> Pages)>();

        foreach (var page in pages)
        {
            if (page.Lines.Count == 0) continue;
            var top = HeaderParser.ReadTopLine(page.Lines[0].Text());
            if (top is null) continue; // trailer / non-race page

            if (card.Track is null)
            {
                card.Track = top.Track;
                card.Date = top.Date;
                card.Product = top.Product;
            }

            var idx = groups.FindIndex(g => g.Number == top.Number);
            if (idx < 0) groups.Add((top.Number, new List<PageText> { page }));
            else groups[idx].Pages.Add(page);
        }

        foreach (var (number, grpPages) in groups)
        {
            grpPages.Sort((a, b) => a.Number.CompareTo(b.Number));
            var firstPage = grpPages.OrderBy(p => p.Lines[0].Baseline).First();

            var race = new Race
            {
                Number = number,
                Header = HeaderParser.Parse(firstPage, number),
            };
            foreach (var page in grpPages)
                race.Horses.AddRange(HorseParser.ParsePage(page));

            card.Races.Add(race);
        }

        card.Races.Sort((a, b) => a.Number.CompareTo(b.Number));
        return card;
    }
}
