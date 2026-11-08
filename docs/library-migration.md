# Library migration (Plex → Vora)

**Settings → Account → Import from Plex** (`PlexImportPanel`) copies a Plex account's watch state and ratings onto a Vora profile. Every profile has it: the person signs in to their own Plex account (the plex.tv PIN flow), so the history is theirs. `LibraryMigrationManager.StartImportAsync` checks the request and `LibraryMigrationJobRunner` runs the job in the background, sending progress over SignalR (`LibraryMigrationUpdated`) to the account that started it and to nobody else; `GET /api/library-import/jobs/{id}` answers only that account. The Plex side is `PlexLibrarySyncProvider` (an `ILibrarySyncProvider`), and the database side is `LibraryMigrationRepository`.

## Who can import what

There used to be an admin page, Admin → Library Migration, that mapped every Plex user to a Vora profile from the admin's Plex sign-in. When plex.tv refused to switch to a user, it quietly read the admin's own history instead, and discussion #244 ended with three profiles holding the admin's history. The page is gone, and so are its `/api/library-migration` routes.

- **Your own history into your own profile.** The default. The job reads the signed-in Plex account (`LibraryMigrationMappingInput.SignedInAccount`).
- **A Plex Home user's history into another profile on the account.** For a child with a managed Plex user and no Plex sign-in of their own: the account's main profile (`UserProfile.IsAdmin`) connects its own Plex account, picks the Home user under **Whose Plex history** (`POST /api/library-import/providers/{id}/accounts`, the signed-in account marked `isSignedIn`), enters that user's Plex PIN if it has one, and picks the profile under **Import into**. Any other profile can only import into itself, and no profile can import into one on another account (`StartImportAsync` refuses with 403).
- **Switching to the Home user never falls back.** `ResolveServerTokenAsync` switches users on plex.tv (`POST /api/v2/home/users/{uuid}/switch`) and then asks plex.tv who the new sign-in is; if the switch is refused, sends no sign-in, or lands on anyone else, the import fails with that reason and writes nothing. Each Plex user then reads the server with the server's own token from that user's resource list (`accessToken`), and a user who can't see the server is told so.
- **Set admin ratings** is offered only to a server admin importing their own history into their own profile.

## Reaching the Plex server

plex.tv lists several addresses for each server: local ones (`https://192-168-0-10.<hash>.plex.direct:32400`), remote ones and a Plex relay. Vora has to reach one of them from where it runs, which in Docker is the container, not the browser. `PlexLibrarySyncProvider.FindReachableConnectionAsync` re-reads the server's addresses from plex.tv and tries them all at once.

- **Order.** Local addresses come first (HTTPS before HTTP), then each local IPv4 address over plain HTTP by its IP, then remote addresses, then the relay (`PlexServerAddresses.InTryOrder`). Trying the plain IP gets past a router or DNS filter that blocks `plex.direct` names. Plain HTTP is never tried over the internet.
- **Probe.** Each address gets one `GET /library/sections` with the account's token, through the `PlexLibrarySyncProbe` client: 5 seconds and no retries. The standard client retries each failure three times, which took about 10 seconds for one refused address in the issue's log, and can take its whole 30-second budget on one that never answers. The best address in that order that answers wins, even when a later one answered first.
- **Nothing answers.** `RemoteServerUnreachableException` becomes a 502 whose detail names every address and why it failed ("connection refused", "its name did not resolve", "no answer within 5 seconds", "Plex refused this account, status 401"). The import panel shows it. Before this, the page sent only the first address Plex listed, and a refused connection came back as a bare 500 (issue #104).
- **Who picks.** The browser sends the Plex server's id, never an address. Listing libraries and the job itself (`LibraryMigrationJobRunner.FindConnectionAsync`) each find the address on the server, so the `/api/library-import` routes can't be pointed at an arbitrary URL. A job that can't reach the server fails with the same message.

## Matching

Nothing is matched by title or file path. Plex and Vora agree on provider ids, so:

- **Movies** match on the item's own TMDB, then IMDb, then TVDB id.
- **Episodes** match on the **show's** id plus season and episode number (`FindEpisodeMatchesAsync`), then fall back to the episode's own TVDB / IMDb / TMDB id (`FindEpisodeMatchesByOwnIdsAsync`).
- A Vora episode that covers a range (`Episode.EndEpisodeNumber`, a multi-episode file) answers for every number in it. Plex lists `S05E09` and `S05E10` separately; both land on the one Vora episode for `S05E09-E10`, and their watch states and ratings are merged (played if either was, the latest rating).
- Anything left over is counted as skipped, and up to 40 are listed on the job as `watch episode tmdb:<show> S<n>E<n> ep-tvdb:<id>`.
- **Reported counts.** Per kind the job reports the Plex entries (`WatchStatesFetched`), the Vora titles they landed on (`WatchStatesImported`) and the entries that landed on a title another entry also matched (`WatchStatesMerged`): both halves of a multi-episode file, or the same title in two Plex libraries. Those are imported, not missing; only entries with no match are skipped. The page used to show "13273 imported / 13360 fetched", which read as 87 failures when every entry had matched (84 were halves of multi-episode files). A kind that wasn't selected reports 0 fetched.

## Multi-episode files

A file named `S05E09-E10`, `S05E09 - E10`, `S05E09E10` or `S02E01-E02-E03` holds every episode in the range. The scanner records the first number as `EpisodeNumber` and the last as `EndEpisodeNumber`, which also feeds episode counts (`docs/scanning-and-tasks.md`).

- **One pattern.** `LocalScannerPatterns.TvEpisode` (`Vora.Plugins`) is the scanner's default. A new TV library stores no pattern (Automatic) and reads with it. It keeps the older absolute form (`S01E01 - 001 - Title`). How a name it cannot read is still placed (other namings, the `.nfo`, the season folder) is in `docs/episode-file-names.md`.
- **Old defaults still work.** TV libraries used to have a pattern with no end-episode group copied into `ScannerRegex`, so `S05E09-E10` read as episode 9 titled "E10 - …", and no episode had a range. `LocalScannerPatterns.EffectiveTvEpisode` treats any former built-in default as "use the current default", and saving the library stores Automatic instead. A pattern the admin wrote is kept as is.
- **Known files get repaired.** A scan only ingests files it hasn't seen, so every TV scan also reads the range from the names of the files it already knows under the scanned folders (`RepairEpisodeRangesAsync`, names only, no disk access). **Scan Library** runs it from `DiscoverTvScanUnitsAsync`, because the parallel library scan only hands new files to `ScanTvUnitAsync`; show, season and episode rescans run it from `ProcessTvDirectoriesAsync`. It then sets `EndEpisodeNumber` where it is missing or different (`IMediaIngestionService.SetEpisodeRangesAsync`). A new file for an existing episode can widen its range (`WidenEpisodeRangeAsync`), never narrow it.

After this landed, a library scan followed by running the migration again picks up episodes that used to be skipped.

## Ratings and poster overlays

A profile's ratings go to `UserMediaRatings`. With **Set admin ratings** ticked, they also become the item's `ServerAdminRating`, which is the rating poster overlays draw. The import marks each item whose server rating changed as needing a new overlay (`LastOverlayGeneratedAt = null`) and queues one **Generate Poster Overlays** task per library it touched. That sync redraws only the items marked this way, rather than queueing a task per item. Ratings set from the app queue the item's overlay the same way (`UserMediaStateManager`). An import run before this marking existed left server ratings that a later run finds unchanged, so it marks nothing; the overlay layout bump to `v9` redraws those posters once (`docs/artwork-image-cache.md`).

## Starting a profile over

An import into the wrong profile (discussion #244: the admin ran the import for each of three Plex users and every Vora profile got the admin's history) is undone from **Users & Access**: each profile tile has **Clear history**, admins only. The dialog shows what the profile has (`GET /api/users/profiles/{id}/viewing-history` → `ViewingHistoryVM`: titles watched, in progress, ratings) and clears the watch history, the ratings or both (`POST …/viewing-history/clear` with `{ watchHistory, ratings }` → `ClearedViewingHistoryVM`; nothing picked is a 400).

- **What goes.** The profile's `UserMediaStates` (played marks, resume points, Continue Watching) and `UserMediaRatings`, which are the two tables an import writes. Its `PreservedUserMediaData` loses the same halves, and a row with nothing left is deleted, so a title brought back from Media Trash doesn't restore the history that was just cleared. Music, podcasts, playlists and the watchlist are left alone.
- **What stays.** A title's `ServerAdminRating` (from **Set admin ratings**, or ratings the admin sets in the app) belongs to the server, not to a profile, so clearing the admin's own ratings doesn't touch it or the poster overlays.
- **After.** `UserMediaStateUpdated` goes to the profile's apps, so a web Home page that is open reloads its rows. The person can then import their own history again from Settings, or the account's main profile can import a Plex Home user's history for them.
