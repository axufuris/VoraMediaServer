# Home video libraries

A Home Video library (`LibraryType.HomeVideo`) holds a family's own recordings. Every file is its own video, named after the file and dated from it, and nothing about it is looked up online. Until this was added, Home Video libraries could be created but no scan path handled them, so they stayed empty.

## What a scan adds

`VoraLocalMediaScannerProvider.ScanHomeVideoLibraryAsync` lists the library's new files, leaving out excluded files and files already in the library. Each one goes through `IngestHomeVideoFileAsync`:

- **One video per file.** `IMediaIngestionService.AddHomeVideoAsync` always creates a new `Movie` row in the library. There is no title or id matching, so ten `IMG_0001.MOV` files from ten phones are ten videos.
- **Title** is the file name without its extension (`HomeVideoNames.Title`). It is never cleaned up: the name the family gave the file is the name shown.
- **Date** (`HomeVideoNames.Date`) is the first whole date in the name, stored as `ReleaseDate`. Recognised forms are `2019-07-04`, `2019.07.04`, `2019_07_04`, `2019 07 04` and `20190704`, which covers `IMG_20190704_123456`, `VID-20190704-WA0001`, `PXL_…` and similar. The separator must be the same on both sides, and the date must exist, so `20190231` doesn't count. With no date in the name, it is the file's last-modified date (UTC); with neither, there is no date.
- **Resolution** comes from a `1080p` / `4k` / … tag in the name, as for films.

**Formats.** `MediaFileExtensions.HomeVideo` is the video list plus the camera and phone formats: `.mov`, `.mts`, `.m2ts`, `.3gp`, `.mpg`, `.mpeg`, `.wmv`, `.webm`. Only Home Video libraries use it, through the scanner and `FolderWatcherService.ExtensionsFor`. Movie and TV libraries keep the narrower list, so a Blu-ray folder's `.m2ts` streams are not ingested as films. `IsMedia` includes the wider list so the watcher still sees deletions. Playback needs nothing extra: a container the client doesn't list is remuxed or transcoded as usual.

## How it is reached

- **Scan Library.** `LibraryManager.DiscoverScanUnitsAsync` returns no units for a Home Video library, so the workflow takes the whole-library path: `TriggerLibraryFolderAndFileScanAsync` → `ScanHomeVideoLibraryAsync`, then enrichment and the usual post-scan passes (analysis, scrub-bar thumbnails, overlays).
- **Folder watcher.** A new file is queued with `QueueScanNewFile` → `TriggerFileScanAsync` → `ScanHomeVideoFileAsync`. That returns a `ScanFileResult`, so the per-item post-processing runs as for a film. Deletions go through `QueueRemoveOrphanedMedia` like any video.

## Nothing is looked up online

`LibraryCapabilities.LooksUpMetadataOnline` is false for Home Video. `MetadataFetchService.StaysLocal` then applies, whatever provider ids the library row holds (an API-created library defaults to `tmdb_metadata`, and an empty artwork id falls back to TMDB artwork):

- **Text metadata** comes from the local provider only, so an NFO beside the file still applies.
- **Artwork** is only what sits beside the file (`FindLocalArtworkAsync`). `FetchProviderArtworkAsync` is skipped.
- **Ratings** are never fetched.

Without this, enrichment would search TMDB for a film called "Beach Day".

## No intro or credits markers

`LibraryCapabilities.DetectsMarkers` is true only for Movie and TV:

- Creating or saving a Home Video library stores intro, credits and preview detection as off.
- The analyzer reads the library type along with the toggles (`MarkerDetectionGateDto.DetectsIntro` / `DetectsCredits`, and the library-level check in `TriggerLibrarySilenceDetectionAsync`). So a library saved before this change, with the server's defaults switched on, still finds no markers on a scan. Otherwise the credits detector would mark the dark end of a video as credits, and auto-skip would cut it.

An admin's forced **Analyze** on one item still runs, as it does for any library with the toggles off. Scrub-bar thumbnails stay available (`HasVideoContent`).

## In the client

`LibraryPage` opens a Home Video library sorted by **Date, newest first**, unless the viewer already saved a sort for that library (`vora_library_sort_{id}`). The sort menu calls `releaseDate` "Date" there instead of "Release Date". Cards are the usual `MediaCard`. Without a poster beside the file, they show the placeholder; no frame from the video is used as a poster.

## Tests

| What | Where |
| --- | --- |
| Names, dates, file dates, one video per file, formats, exclusions, watcher single file | `tests/Vora.Application.Tests/Scanner/HomeVideoScanTests.cs` |
| No TMDB text, artwork or ratings | `tests/Vora.Application.Tests/Metadata/HomeVideoMetadataTests.cs` |
| Marker gate by library type | `MediaAnalyzerManagerDetectionTests.A_scan_looks_for_markers_only_in_libraries_that_have_them` |
| Detection toggles saved off | `LibraryManagerTests.A_home_video_library_never_looks_for_intros_or_credits` |
| `AddHomeVideoAsync` never matches | `MediaIngestionServiceTests.A_home_video_is_always_a_new_video_dated_as_given` |
| Watcher formats by library type | `FolderWatcherReconciliationTests.A_home_video_library_takes_camera_formats_a_movie_library_leaves_alone` |
| Default sort | `src/Vora.Web/src/pages/Client/LibraryPage.test.tsx` |
