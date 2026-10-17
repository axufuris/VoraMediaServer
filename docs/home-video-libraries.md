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

## Posters from the video itself

When nothing sits beside a home video, `MetadataFetchService.FindLocalArtworkAsync` asks `VideoFrameArtworkService` for a poster and a backdrop, whichever is missing. The result goes into the artwork list like any other source (`ProviderId` `video_frame`), so `ApplyArtworkAsync` sets it, the artwork picker lists it, and a locked or uploaded poster still wins. Every path that refreshes artwork reaches it: library enrichment, the folder watcher's new file, and a manual refresh.

- **Which frame.** The service takes one third of the way in, then halfway, then two thirds. It keeps the first frame whose mean luminance (`MeanLuminance`, on a 16×16 copy) is at least 0.08, so a fade-in or a lens cap is passed over; if every frame is dark, it keeps the brightest. The duration comes from analysis when known, else from `ffprobe`. A clip of a second or less, or one whose length can't be read, uses its first frame. Frames come from the existing `IVideoThumbnailGeneratorService.ExtractFrameAsync` (the episode-still grabber), so no GPU flags are involved.
- **Shapes.** `Compose` draws the whole frame, fitted and centred, over a blurred and darkened copy of itself that fills the canvas. The poster is 720×1080 (2:3) and the backdrop 1920×1080, so a landscape video and a portrait phone video both show the whole picture. The blur runs on a one-eighth copy that is then scaled up, which keeps it cheap at backdrop size.
- **Kept, not redone.** The files are `media_{itemId}_{partId}_frame_poster.jpg` and `…_frame_backdrop.jpg` under `StoragePaths:CustomArtwork`, served from `/api/artwork/custom/`. Once they exist, a later refresh reuses them without running ffmpeg. They are written to a temp name and then moved into place. Files no row points to any more are cleared by **Remove Unused Files**.
- **A file that won't give a frame** gets no artwork and a logged warning. The next refresh tries again.

## No intro or credits markers

`LibraryCapabilities.DetectsMarkers` is true only for Movie and TV:

- Creating or saving a Home Video library stores intro, credits and preview detection as off.
- The analyzer reads the library type along with the toggles (`MarkerDetectionGateDto.DetectsIntro` / `DetectsCredits`, and the library-level check in `TriggerLibrarySilenceDetectionAsync`). So a library saved before this change, with the server's defaults switched on, still finds no markers on a scan. Otherwise the credits detector would mark the dark end of a video as credits, and auto-skip would cut it.

An admin's forced **Analyze** on one item still runs, as it does for any library with the toggles off. Scrub-bar thumbnails stay available (`HasVideoContent`).

## In the client

`LibraryPage` opens a Home Video library sorted by **Date, newest first**, unless the profile has a library sort of its own or the viewer picked one for that library (see "Library sort" in `docs/frontend-client-ui.md`). The sort menu calls `releaseDate` "Date" there instead of "Release Date". Cards are the usual `MediaCard`, showing the frame poster above.

## Tests

| What | Where |
| --- | --- |
| Names, dates, file dates, one video per file, formats, exclusions, watcher single file | `tests/Vora.Application.Tests/Scanner/HomeVideoScanTests.cs` |
| No TMDB text, artwork or ratings; frame artwork only when nothing sits beside the file | `tests/Vora.Application.Tests/Metadata/HomeVideoMetadataTests.cs` |
| Frame choice, poster and backdrop shapes, portrait videos, reuse | `tests/Vora.Application.Tests/Metadata/VideoFrameArtworkServiceTests.cs` |
| Marker gate by library type | `MediaAnalyzerManagerDetectionTests.A_scan_looks_for_markers_only_in_libraries_that_have_them` |
| Detection toggles saved off | `LibraryManagerTests.A_home_video_library_never_looks_for_intros_or_credits` |
| `AddHomeVideoAsync` never matches | `MediaIngestionServiceTests.A_home_video_is_always_a_new_video_dated_as_given` |
| Watcher formats by library type | `FolderWatcherReconciliationTests.A_home_video_library_takes_camera_formats_a_movie_library_leaves_alone` |
| Default sort | `src/Vora.Web/src/pages/Client/LibraryPage.test.tsx` |
