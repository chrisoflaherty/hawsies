# BrisPP

Parses BRIS / Brisnet **"Premium Plus PP's"** Past Performance PDFs into structured,
auditable data: each race, its horses with their owner / trainer / jockey, and every
horse's past-performance lines, workouts, and trainer angles.

The PDF uses a custom font (`PP2A-Regular`) that reuses Latin-1 code points as private
glyphs — superscript digits, fractions, surface markers, and so on. BrisPP decodes
these but **keeps the original tokens alongside the decoded values**, so every
translation can be audited rather than trusted blindly.

## Layout

| Project | Role |
|---|---|
| `src/BrisPP.Core` | The library: PDF text extraction, column reconstruction, parsing, domain model, JSON serialization. |
| `src/BrisPP.Cli`  | A console app wrapping the library (parse to JSON, plus diagnostic commands). |
| `tests/BrisPP.Tests` | xUnit tests, including end-to-end coverage against a sample card. |

Inside `BrisPP.Core`:

- `Pdf/` — text extraction via [PdfPig](https://github.com/UglyToad/PdfPig).
- `Layout/` — reconstructs logical rows from scattered glyphs (`PageText`, line/strip clustering).
- `Glyphs/` — the private-glyph decoder (`BrisGlyphs`).
- `Parsing/` — `RaceCardParser`, `HeaderParser`, `HorseParser`, `RunningLineParser`.
- `Model/` — the data model (`RaceCard`, `Race`, `Horse`, `PastPerformanceLine`, …).
- `Json/` — `CardJson`, the serializer.

## Requirements

- **.NET 10 SDK**. If you installed it via Homebrew, make sure it's on your `PATH`:
  ```sh
  export PATH="/opt/homebrew/bin:$PATH"
  ```
- Dependencies (`UglyToad.PdfPig`) restore automatically on first build.

## Build & test

```sh
dotnet build
dotnet test
```

The PDF-backed tests need a sample card. They look for it at, in order:

1. the path in the `BRIS_SAMPLE_PDF` environment variable, then
2. `~/Downloads/productdownload.pdf`.

If neither exists, those tests no-op (they return early rather than fail), so the
suite still passes without the sample on hand.

```sh
BRIS_SAMPLE_PDF=/path/to/card.pdf dotnet test
```

## CLI usage

```
dotnet run --project src/BrisPP.Cli -- <command> <pdf> [args]
```

| Command | Description |
|---|---|
| `json  <pdf> [outFile]` | Parse the card and write structured JSON. With no `outFile`, prints to stdout. |
| `parse <pdf> [raceNo] [horseNo]` | Parse and print a human-readable summary. Optional filters narrow output to one race / horse. |
| `dump  <pdf> [pageNumber]` | Print the reconstructed logical lines for a page (diagnostic). |
| `chars <pdf> <pageNumber> <topMin> <topMax>` | Print glyph X positions in a baseline band (diagnostic). |
| `strip <pdf> <pageNumber> <xMin> <xMax> <yMin> <yMax>` | Print a single column strip (diagnostic). |

Examples:

```sh
# Write the whole card to JSON
dotnet run --project src/BrisPP.Cli -- json ~/Downloads/productdownload.pdf card.json

# Inspect race 1, horse 1 in readable form (past performances, workouts, angles)
dotnet run --project src/BrisPP.Cli -- parse ~/Downloads/productdownload.pdf 1 1
```

`BRIS_TOL` (default `0.2`) tunes the glyph baseline-overlap tolerance used during
extraction; you rarely need to change it.

## JSON output

The serializer drops nulls, writes enums as names, and emits each value's raw token
next to its decoded form. For example a past-performance distance and running-line
call look like:

```json
"Distance": { "Raw": "1ˆ", "Furlongs": 8.5, "Display": "1 1/16m", "Surface": "Turf" },
"Start":    { "Raw": "7­", "Position": 7, "Lengths": 6, "Display": "76" }
```

`Raw` is exactly what was on the page (`1ˆ`, `7­`); the sibling fields are the decoded
interpretation. The same pattern applies to times, margins, pace figures, and so on.

### Using the library directly

```csharp
using BrisPP.Core.Pdf;
using BrisPP.Core.Parsing;
using BrisPP.Core.Json;

var pages = PdfTextExtractor.Extract("card.pdf");
var card  = RaceCardParser.Parse(pages);

Console.WriteLine($"{card.Track} {card.Date}: {card.Races.Count} races");
File.WriteAllText("card.json", CardJson.Serialize(card));
```

## Known limitations

The source PDF scatters descender letters (p/q/g) and punctuation onto adjacent
micro-rows, so a few names render with gaps (e.g. `Christo her` for *Christopher*).
The raw text is always preserved. Two residual edge cases on **bonus** fields remain
(the core race / horse / connection / past-performance data is complete for every
horse in the sample card):

- One horse's **sex** letter scatters onto its name row, leaving color and age intact
  but sex blank.
- Six foreign-shipper horses on large-field continuation pages lack a **Prime Power**
  rating, because that micro-row falls outside the identity zone.

Sale and claiming-price data visible in the raw rows is not yet modeled.
