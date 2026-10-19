# Movie file names

How a movie file becomes a film in a library, how a new file finds the film it belongs to, and how a scan takes apart files an older scan put on the wrong film. Episodes have their own rules in [`episode-file-names.md`](episode-file-names.md).

## Reading the file

`VoraLocalMediaScannerProvider.ResolveMovieIdentity` reads a file in this order. Every movie file goes through it: the full scan (`ProcessMovieDirectoriesAsync`), a library scan's units (`ScanMovieUnitAsync`), and the folder watcher's single files.

1. **The file name**, through the library's pattern or the default `Title (Year) {imdb-tt…}` pattern (`BuildMovieRegexes`). This gives the title, the year and a `{tmdb-…}` / `{imdb-…}` / `{tvdb-…}` id.
2. **The movie folder**, when the file name has no year (`MovieFolderNaming`). This applies when the file's folder is named `Title (Year)` or is a release name with a year (`The.Mummy.2004.1080p.BluRay`, read as below), is not a library folder itself, and either:
   - holds one film (`LocalMediaAssets.HoldsOneFilm`: every non-extra video shares the file's `FilmKey`), or
   - has the same title as the file (`FilmKey`).

   The file then takes the folder's title and year. It also takes any ids the file name leaves out, from `{imdb-…}` or `[tmdbid-…]` tags in the folder name (`LocalIdentity.TaggedIds`); ids in the file name win. So `Heat (1995)/heat.1995.1080p.bluray.mkv` is Heat (1995). In `Heat (1995)/` holding `Heat.mkv` and `Heat Bonus Disc.mkv`, only `Heat.mkv` gets 1995.
3. **A release name**, when there is still no year (`ReleaseNames.Read`). `Lee.2024.1080p` was searched on TMDB as that whole string and matched nothing. Now:
   - **Separators.** A name with no spaces has its dots and underscores read as spaces.
   - **Where the title ends.** The title runs up to the first release tag (`1080p`, `4k`, `BluRay`, `WEB-DL`, `x264`, `HEVC`, `S01`, …) or the last real year before it. The year must be between 1900 and next year, and can't be the first word: `Blade.Runner.2049.2017` is Blade Runner 2049 (2017), `2001.A.Space.Odyssey.1968` and `1917.2019` keep their numbers.
   - **Acronyms.** A run of single letters is joined back up: `S.W.A.T.2003` is S.W.A.T. (2003), `A.I.Artificial.Intelligence.2001` is A.I. Artificial Intelligence.
   - **Names that aren't release names are left alone.** A dotted name needs a year or a tag, so `A.C.O.D.`, `Slasher.com` and `Waiting...` stay as they are. A name with spaces needs a release tag, so `Heat 1995 1080p` is Heat (1995) but `Wonder Woman 1984` and `Death Race 2000` keep their titles.

   The same reading applies to a film's extras folder (`IngestMovieExtraAsync`) and to TV show folders (`ResolveShowIdentity`, so `Severance.2022.1080p.WEB-DL` is Severance (2022)). Episode titles from release names are cleaned the same way (`docs/episode-file-names.md`). Music is named from its tags, so it doesn't go through this; a track with no title tag is titled from its raw file name. Home videos keep their file name as written. It also applies to the metadata search (`MediaMatchIds.SearchTerms`, used by `MetadataFetchService` and the Fix-match default query). That way an item an older scan saved under its whole release name is matched on its next refresh, and its title is then replaced by the provider's.
4. **An NFO**, when the library reads them. `LocalIdentity.Trusted` drops a document whose year is more than a year away from the year found so far, or whose ids contradict the name's.

A year in the file name always wins over the folder's, and a `Title (Year)` folder wins over a release name in the file.

## Naming an extra

A local trailer or extra (`-trailer`, a `Trailers` folder, …) is stored under its file name, and is named for display when the title's details are read (`ExtraNames`, from `MediaManager`), so a rename reaches extras already in the library without a rescan. A name that is the film's own (a release name with a year, `Title (Year)`, the title itself, nothing at all, or just the kind of extra) reads as the title and the kind: `The.Movie.2007.1080p-trailer.mkv` is **The Movie Trailer**, using the film's metadata title, and the same for a show's extras. Any other name is kept, made readable: `Making.of.the.Dragon.1080p` is Making of the Dragon.

## Finding the film

`MediaIngestionService.EnsureMovieAsync` returns an existing film, or creates one, in this order:

1. **Hand-matched sibling.** When a known file beside it with the same `FilmKey` belongs to a film fixed by hand (Fix match), the new file joins that film (`FindMovieMatchedByHandAsync`).
2. **External id.** `GetMovieIdByExternalIdAsync` looks for the TMDB or IMDb id in the library.
3. **Title + year.** `GetMovieIdByTitleAndYearAsync` compares titles normalized by `NormalizeTitle` (lower case, letters and digits only), so a title metadata rewrote still matches (`Avatar The Way of Water` / `Avatar: The Way of Water`). It skips a film whose stored TMDB or IMDb id **contradicts** the file's (`LocalIdentity.Contradicts`). That is how two films with the same name and year stay apart when their files carry different ids. A film with no stored ids still takes the file.
4. **Title without a year.** This matches only when exactly one film has the title, as `GetTvShowIdByTitleAndYearAsync` already did for shows. `Heat.mkv` no longer joins whichever Heat came first.
5. Otherwise, a new film with the file's title, year and ids.

## Repairing films that hold another film's file

`SeparateFilesOfOtherFilmsAsync` runs at the start of a movie library scan (`DiscoverMovieScanUnitsAsync`) and of `ProcessMovieDirectoriesAsync`. It works from `GetFilesOfMoviesWithSeveralFilesAsync` and skips:

- films matched by hand (`Match` in `LockedFields`);
- films with no stored ids;
- files that are no longer in the library, outside the folders being scanned, or gone from disk.

For each film with two or more such files, it reads each file as above. Files whose ids contradict the film's are taken off with `RemoveMediaItemByPathAsync`, and the same scan adds them again as the film their name says.

It leaves a film alone when:

- no file contradicts it. Files with no ids, or with the film's own ids, always stay.
- **every** file contradicts it. The stored ids are then the odd ones out (a wrong Fix match or a wrong metadata match), and moving every file would only rebuild the same film.

## Tests

| What | Where |
| --- | --- |
| Folder fallback, repair pass | `tests/Vora.Application.Tests/Scanner/MovieIdentityScanTests.cs` |
| Release names (films, release folders, show folders, names left alone) | `tests/Vora.Application.Tests/Scanner/ReleaseNameTests.cs` |
| Searching an item saved under its release name | `tests/Vora.Application.Tests/Metadata/ReleaseNameSearchTests.cs`, `MediaMatchIdsTests` |
| NFO identity, hand-matched siblings | `tests/Vora.Application.Tests/Scanner/LocalMetadataScanTests.cs` |
| Title + year with ids, no-year ambiguity, files of films with several | `tests/Vora.Infrastructure.Tests/MediaRepositoryMovieMatchTests.cs` |
| `EnsureMovieAsync` passes the file's ids | `tests/Vora.Application.Tests/Media/MediaIngestionServiceTests.cs` |
| Extra names | `tests/Vora.Application.Tests/Media/ExtraNamesTests.cs` |
