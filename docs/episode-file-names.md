# Episode file names

How the TV scanner decides which episode a file is. `EpisodeFileNames` (`Vora.Plugins/Providers/Local`) reads the name and places the file; `VoraLocalMediaScannerProvider.IngestTvFileAsync` uses the placement for every new file, the folder watcher's single-file scan included.

## The order

1. **The library's pattern.** The `ScannerRegex` an admin wrote, or `LocalScannerPatterns.TvEpisode` when none was written (a stored copy of an older built-in default reads as the current one, `EffectiveTvEpisode`). When it reads a season and episode, that is final: the numbers, the range, the title and the air date all come from it. Every Sonarr name in the `Show (Year) - S01E01 - Title [quality]` style is read here, the same way as before.
2. **Any common naming** (`TvEpisodeAnyNaming`, adapted from [jc21's tvshowregex](https://github.com/jc21/tvshowregex)). Explicit markers: `S01E01`, `s01x01`, `1x01`, `Season 1 Episode 1`, `Series 1 Episode 1`, `S01 EP01`, and their ranges. An explicit marker may start the name (`1x01 - Pilot`, `S02E09`); anything else needs a show name before it, so the `12` of `12 Monkeys` is never a number. The **first** marker counts, so a `.1920x1080` later in the name cannot overwrite `S01E01`.
3. **The `.nfo` beside the file** (`LocalNfoReader.ReadEpisodeNumbers`): `<season>` and `<episode>` of its `<episodedetails>`, the lowest to the highest episode for a multi-episode file. Sonarr, Kodi and Jellyfin write these. It is read **whatever the library's local metadata setting is**, because it only places the file; titles and the rest still follow that setting (`docs/local-metadata.md`).
4. **Guesses**, used only when nothing above gave numbers:
   - a bare `101` or `1.01` (season 1, episode 1);
   - an absolute number (`001`, `Ep01`, `Episode 001`, `E001`), as that episode of the folder's season;
   - an air date (below);
   - a number that starts the name (`05 - Title`), only inside a season folder, so a show called `24` is not episode 24.
5. **Nothing to go on**: the file is left out and a warning names it. It used to be filed as season 1 episode 1, so every unreadable file in a show became another version of S01E01 (issue #103).

**Season**: from the name; otherwise from the folder (`Season 2`, `Season 02`, `season.3`, `Series 4`, `Specials` is 0, `SeasonOfFolder`); otherwise 1.

**Air dates** (`ParseAirDate`): `2024-01-15`, `2024.01.15`, `2024_01_15`, `15-01-2024` and `01-15-2024` (a day over 12 decides which is which; `03-04-2024` cannot be told apart and is not used), `Jan 15 2024`, `January 15, 2024`. A file with only a date becomes episode `yyyymmdd` (20240115) of its folder's season, or of the year outside a season folder. Every day is its own episode, sorted by date, and a provider never matches it to a numbered episode by mistake.

## What the default pattern reads

`TvEpisode` is the first step and it was widened, not replaced:

| Name | Read as |
|---|---|
| `Show (2021) - S01E01 - Title [WEBDL-1080p][EAC3 5.1]-GRP` | S01E01, "Title" |
| `Show - S01E01 - 001 - Title` | S01E01, absolute 1, "Title" |
| `S05E09-E10`, `S06E17 - E18`, `S06E17E18`, `S02E01-E02-E03` | ranges, as before |
| `S01E01-S01E02`, `S01E01-02`, `S01E01 & S01E02`, `S01E01+E02`, `S01E01_E02` | ranges (new: `& S01E02` used to read as episode 2, `-02` as an absolute number) |
| `Show - S01E01 - 2024-01-15 - Title` | S01E01 on 2024-01-15, "Title" (new: the year used to be an absolute number) |
| `Grey's Anatomy - S14E09 - 1-800-799-7233` | the whole title (new: `1-` used to be taken as an absolute number) |
| `2024-01-15 - Title` | that date, "Title" |
| `2024-01-15 - 001 - Title` | that date, absolute 1, "Title" (new: "001 - Title" used to be the title) |

## Files an older scan stacked on S01E01

Every TV scan (library, show, season, episode and the parallel library scan's discovery) runs `SeparateFilesStackedOnTheFirstEpisodeAsync` before it repairs ranges and lists new files:

- It looks only at files stored on season 1 episode 1 (`GetFirstEpisodeFilePathsAsync`) inside the folders being scanned.
- A file whose name or `.nfo` now places it elsewhere is taken off (`RemoveMediaItemByPathAsync`; the episode is removed with its last file) and dropped from the known files, so the same scan ingests it as a new file, as the episode it is, with metadata.
- A file that reads as S01E01 stays, as does one still unreadable. A file that is gone from disk is left for Media Trash.

A library scanned before this change is repaired by its next scan, with nothing to delete or re-add.

## Ranges of known files

`RepairEpisodeRangesAsync` reads the range of every known file (explicit markers only) and `SetEpisodeEndNumbersAsync` records it only on the episode the file **starts** at (`EpisodeRange(FirstEpisode, LastEpisode)`), so a file still stacked on S01E01 cannot stretch that episode's range.

## Tests

- `EpisodeFileNameTests`: every line of jc21's `cases.txt`, copied verbatim to `tests/Vora.Application.Tests/Scanner/tvshowregex-cases.txt`. Two lines put an absolute number before the `S01E01` marker; the default reads those first and keeps no absolute number, which the test lists. Also Sonarr names that must read as they always have, numeric titles, every range form, a custom pattern deciding first, the placement order, season folders and dates.
- `VoraLocalMediaScannerProviderTests`: a Sonarr-default season becomes one episode per file; an `.nfo` places a name without numbers with local metadata off; an unreadable file is left out; stacked files are separated while a real S01E01 stays.
- `EpisodeRangeTests`: a range is recorded only where its file starts; the first-episode query.
- Before merging, the change was run over every episode file name on the QA server (25,130): the same season, episode and range for each, nothing taken off S01E01, and 7 numeric titles read in full.

Thanks to jc21 for the issue, the naming survey and the test cases this is built on.
