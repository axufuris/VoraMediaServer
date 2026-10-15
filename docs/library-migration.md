# Library migration (Plex → Vora)

Admin → Library Migration copies each mapped Plex account's watch state and ratings onto a Vora profile. `LibraryMigrationJobRunner` runs the job in the background and pushes progress over SignalR. The Plex side is `PlexLibrarySyncProvider` (an `ILibrarySyncProvider`), and the database side is `LibraryMigrationRepository`.

## Reaching the Plex server

plex.tv lists several addresses for each server: local ones (`https://192-168-0-10.<hash>.plex.direct:32400`), remote ones and a Plex relay. Vora has to reach one of them from where it runs, which in Docker is the container, not the browser. `PlexLibrarySyncProvider.FindReachableConnectionAsync` re-reads the server's addresses from plex.tv and tries them all at once.

- **Order.** Local addresses come first (HTTPS before HTTP), then each local IPv4 address over plain HTTP by its IP, then remote addresses, then the relay (`PlexServerAddresses.InTryOrder`). Trying the plain IP gets past a router or DNS filter that blocks `plex.direct` names. Plain HTTP is never tried over the internet.
- **Probe.** Each address gets one `GET /library/sections` with the account's token, through the `PlexLibrarySyncProbe` client: 5 seconds and no retries. The standard client retries each failure three times, which took about 10 seconds for one refused address in the issue's log, and can take its whole 30-second budget on one that never answers. The best address in that order that answers wins, even when a later one answered first.
- **Nothing answers.** `RemoteServerUnreachableException` becomes a 502 whose detail names every address and why it failed ("connection refused", "its name did not resolve", "no answer within 5 seconds", "Plex refused this account, status 401"). Both import pages show it. Before this, the page sent only the first address Plex listed, and a refused connection came back as a bare 500 (issue #104).
- **Who picks.** The browser sends the Plex server's id, never an address. Listing libraries and the job itself (`LibraryMigrationJobRunner.FindConnectionAsync`) each find the address on the server, so the self-service `/api/library-import` routes can't be pointed at an arbitrary URL. A job that can't reach the server fails with the same message.

## Matching

Nothing is matched by title or file path. Plex and Vora agree on provider ids, so:

- **Movies** match on the item's own TMDB, then IMDb, then TVDB id.
- **Episodes** match on the **show's** id plus season and episode number (`FindEpisodeMatchesAsync`), then fall back to the episode's own TVDB / IMDb / TMDB id (`FindEpisodeMatchesByOwnIdsAsync`).
- A Vora episode that covers a range (`Episode.EndEpisodeNumber`, a multi-episode file) answers for every number in it. Plex lists `S05E09` and `S05E10` separately; both land on the one Vora episode for `S05E09-E10`, and their watch states and ratings are merged (played if either was, the latest rating).
- Anything left over is counted as skipped, and up to 40 are listed on the job as `watch episode tmdb:<show> S<n>E<n> ep-tvdb:<id>`.
- **Reported counts.** Per kind the job reports the Plex entries (`WatchStatesFetched`), the Vora titles they landed on (`WatchStatesImported`) and the entries that landed on a title another entry also matched (`WatchStatesMerged`): both halves of a multi-episode file, or the same title in two Plex libraries. Those are imported, not missing; only entries with no match are skipped. The page used to show "13273 imported / 13360 fetched", which read as 87 failures when every entry had matched (84 were halves of multi-episode files). A kind that wasn't selected reports 0 fetched.

## Multi-episode files

A file named `S05E09-E10`, `S05E09 - E10`, `S05E09E10` or `S02E01-E02-E03` holds every episode in the range. The scanner records the first number as `EpisodeNumber` and the last as `EndEpisodeNumber`, which also feeds episode counts (`docs/scanning-and-tasks.md`).

- **One pattern.** `LocalScannerPatterns.TvEpisode` (`Vora.Plugins`) is both the scanner's default and what `LibraryManager` gives a new TV library. It keeps the older absolute form (`S01E01 - 001 - Title`). How a name it cannot read is still placed (other namings, the `.nfo`, the season folder) is in `docs/episode-file-names.md`.
- **Old defaults still work.** TV libraries used to have a pattern with no end-episode group copied into `ScannerRegex`, so `S05E09-E10` read as episode 9 titled "E10 - …", and no episode had a range. `LocalScannerPatterns.EffectiveTvEpisode` treats any former built-in default as "use the current default", and saving the library replaces it. A pattern the admin wrote is kept as is.
- **Known files get repaired.** A scan only ingests files it hasn't seen, so every TV scan also reads the range from the names of the files it already knows under the scanned folders (`RepairEpisodeRangesAsync`, names only, no disk access). **Scan Library** runs it from `DiscoverTvScanUnitsAsync`, because the parallel library scan only hands new files to `ScanTvUnitAsync`; show, season and episode rescans run it from `ProcessTvDirectoriesAsync`. It then sets `EndEpisodeNumber` where it is missing or different (`IMediaIngestionService.SetEpisodeRangesAsync`). A new file for an existing episode can widen its range (`WidenEpisodeRangeAsync`), never narrow it.

After this landed, a library scan followed by running the migration again picks up episodes that used to be skipped.

## Ratings and poster overlays

A profile's ratings go to `UserMediaRatings`. With **Set admin ratings** ticked, they also become the item's `ServerAdminRating`, which is the rating poster overlays draw. The import marks each item whose server rating changed as needing a new overlay (`LastOverlayGeneratedAt = null`) and queues one **Generate Poster Overlays** task per library it touched. That sync redraws only the items marked this way, rather than queueing a task per item. Ratings set from the app queue the item's overlay the same way (`UserMediaStateManager`). An import run before this marking existed left server ratings that a later run finds unchanged, so it marks nothing; the overlay layout bump to `v9` redraws those posters once (`docs/artwork-image-cache.md`).
