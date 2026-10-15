# Movie file names

How a movie file becomes a film in a library, how a new file finds the film it belongs to, and how a scan takes apart files an older scan put on the wrong film. Episodes have their own rules in [`episode-file-names.md`](episode-file-names.md).

## Reading the file

`VoraLocalMediaScannerProvider.ResolveMovieIdentity` reads a file in this order. Every movie file goes through it: the full scan (`ProcessMovieDirectoriesAsync`), a library scan's units (`ScanMovieUnitAsync`), and the folder watcher's single files.

1. **The file name**, through the library's pattern or the default `Title (Year) {imdb-tt…}` pattern (`BuildMovieRegexes`). This gives the title, the year and a `{tmdb-…}` / `{imdb-…}` / `{tvdb-…}` id.
2. **The movie folder**, when the file name has no year (`MovieFolderNaming`). This applies when the file's folder is named `Title (Year)`, is not a library folder itself, and either:
   - holds one film (`LocalMediaAssets.HoldsOneFilm`: every non-extra video shares the file's `FilmKey`), or
   - has the same title as the file (`FilmKey`).

   The file then takes the folder's title and year. It also takes any ids the file name leaves out, from `{imdb-…}` or `[tmdbid-…]` tags in the folder name (`LocalIdentity.TaggedIds`); ids in the file name win. So `Heat (1995)/heat.1995.1080p.bluray.mkv` is Heat (1995). In `Heat (1995)/` holding `Heat.mkv` and `Heat Bonus Disc.mkv`, only `Heat.mkv` gets 1995.
3. **An NFO**, when the library reads them. `LocalIdentity.Trusted` drops a document whose year is more than a year away from the year found so far, or whose ids contradict the name's.

A year in the file name always wins over the folder's.

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
| NFO identity, hand-matched siblings | `tests/Vora.Application.Tests/Scanner/LocalMetadataScanTests.cs` |
| Title + year with ids, no-year ambiguity, files of films with several | `tests/Vora.Infrastructure.Tests/MediaRepositoryMovieMatchTests.cs` |
| `EnsureMovieAsync` passes the file's ids | `tests/Vora.Application.Tests/Media/MediaIngestionServiceTests.cs` |
