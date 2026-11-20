# Smart lists (home-screen rows)

A smart list is one row on the client home screen. Admins manage them at **Admin → Smart Lists** (`pages/Admin/SmartLists/`); every client draws the rows the server says are active, in `DisplayOrder`, and a profile can still hide or reorder rows for itself on its own device (`HomeLayoutItem`, see `docs/auth-and-devices.md`).

## Sources

`SmartList.Source` (`SmartListSource`) decides what a row holds:

| Source | What the row shows | Gate |
|---|---|---|
| `Library` | Movies, shows, seasons, episodes or songs, picked by rules (`FilterRulesJson`) or from a collection (`CollectionId`) | — |
| `FavoriteChannels` | The profile's favorite Live TV channels, with what is on now | `EnableLiveTv` |
| `FavoriteStations` | The profile's favorite radio stations | `EnableInternetRadio` |
| `NewPodcastEpisodes` | The newest episodes from the profile's podcast subscriptions | `EnablePodcasts` |
| `RecentlyAddedMusic` | Albums from the music libraries | — |
| `RecentRecordings` | The profile's finished DVR recordings, newest first | `EnableLiveTv` and `EnableDvr` |
| `ContinueListening` | Where the profile's music queues stopped, newest first (`MusicResumeManager`, `docs/music-and-audio.md`) | — |

A row whose feature is off is dropped from `GET /api/smartlists/active` for everyone (admins included — the home screen shows what is turned on) and its entries come back empty.

### Options per source

All sources share `Title`, `SortBy`, `MaxItems` (clamped 1–100 by `SmartListManager`), `ShowOnHomepage`, `ShowToFriends` (off = admins only) and the optional yearly window `ActiveStart/EndMonth/Day` (December → January wraps; an impossible date drops the window on save rather than breaking `/active`). A row built from a collection also follows the collection's Seasonal Visibility: it shows while both windows are open, on today's date in the server's time zone (`CollectionSeason`, `docs/theme-collections.md`). The rest of the options reuse `FilterRulesJson` (`SmartListRulesDto`):

- **Library**: `mediaTypes`, `decade`, `genreIds`, `contentRating`, `unwatchedOnly`, plus `LibraryId` to read from one library. Every `SmartListSortBy` applies; `TitleAsc` sorts by sort title.
  - **Most watched** (`MostWatched`) is the viewer's own: only titles this profile has played, the most-played first. A profile that has played nothing gets an empty row.
  - **Popular** (`Popular`) is the server's: titles ordered by how many different profiles have played them, then by rating (`ThirdPartyRating1`, highest first, unrated last), then by id so the order doesn't shift between requests.
    - A show counts the profiles that played any of its episodes, and one profile playing a title again and again counts once. Trailers and extras (`StreamSession.ExtraId`) and plays with no profile don't count.
    - Titles nobody has played are not left out: they follow the played ones in order of rating, so a server with no play history yet reads as its top rated titles.
    - The count is the same for every viewer (the viewer's library, rating and unrated filters still decide which titles can appear). Nothing here says who played what.
    - It reads the plays, not an index, so it is the dearest of the library sorts: one grouped statement over `StreamSessions` for the ids (`ViewersPerTitle`), then the tiles. On a copy of 4,600 titles and 30,000 plays it took about 26 ms, against about 5 ms for the date rows; it grows with the number of plays, not with the library. Written as a correlated count per title it took 175 ms.
  - A show appears at most once. `DateAddedDesc` sorts by `LastContentAddedAt ?? AddedAt`, so a new episode lifts its season and its show as well. When a list mixes more than one of shows, seasons and episodes and is sorted by date added, `RecentlyAddedTv.OnePerShow` keeps one tile per show:
    - **the show** when the show itself is new (added within two days of its latest content) or its new episodes span several seasons;
    - **the episode** when exactly one episode is new;
    - **the season** when several episodes of one season are new.

    Only episodes added within two days of the show's latest content count as new. A list with one TV level keeps one episode per show (the first unwatched), and a shows-only list still lifts a show when it gets a new episode.

    The repository reads the newest TV rows in pools (`max(maxItems × 10, 100)` rows) and keeps reading, leaving out the shows it has already seen, until it has `maxItems` tiles or runs out of rows. TV rows that were not chosen are left out of the list entirely. A single pool was not enough: a whole show added at once (a show, 31 seasons and 203 episodes) overflowed a 200-row pool, and the rows past it appeared as separate tiles.
- **Favorites**: sort `TitleAsc` (name), `DateAddedDesc` (recently favorited) or `Random`. The same channel in two playlists shows once.
- **Podcasts**: `unwatchedOnly` (only episodes the profile hasn't finished) and `days` (published within). Always newest first.
- **Music**: `LibraryId` (one music library) and sort `DateAddedDesc` → recently added, `ReleaseDateDesc` → newest year, `TopRated` → most popular (Last.fm), `TitleAsc` → artist A–Z.
- **Recordings**: `days` (recorded within). Only `Completed` sessions with an output file.

`SmartListManager.Apply` clears `CollectionId` on anything that isn't a library row and `LibraryId` on anything that isn't library or music, so switching a row's source can't leave a stale filter behind.

## Endpoints

- `GET /api/smartlists/active` → `SmartListClientVM[]` (`id`, `title`, `displayOrder`, `source`).
- `GET /api/smartlists/{id}/entries[?excludeLibraryIds=]` → `SmartListEntryVM[]`, the endpoint clients render. Each entry has `kind` (`Media`, `Channel`, `Station`, `PodcastEpisode`, `Album`, `Recording`), `id`, `title`, `subtitle`, `imageUrl`, and exactly one payload: `media` (`LibraryItemVM`), `channel` (`IptvChannelVM`), `podcastEpisode` (`PodcastFeedEpisodeVM`), `album` (`AlbumVM`) or `recording` (`IptvRecordingSessionVM`) — the same shapes the Live TV, Radio, Podcasts, Music and DVR screens already use, so playing an entry is the code those screens already have.
- `GET /api/smartlists/{id}/items` → `LibraryItemVM[]`, kept for clients that predate sources. It returns `[]` for a non-library row, so an old client simply hides it.
- Admin (`/api/admin/smartlists`, `AdminOnly`): list, create, update, delete, `PUT /reorder`, `GET /defaults` (`SmartListDefaultVM`: key, title, source, `isPresent`) and `POST /defaults/restore`.

Library entries carry `Type = "Track"` for songs (they were `"Unknown"`), with the artist on `LibraryItemVM.Artist` and the album cover as the poster.

A library entry's watched state comes from the same place as the library page's: the projection itself, `LibraryItemVM.ProjectionFor(profileId, states, ratings)`, used by the library page (`MediaRepository.GetLibraryItemsAsync`), `SmartListRepository` and `RecommendationRepository` (the "Because you watched" rows), and by a collection's items (`CollectionDetailsVM.ProjectionFor`). A movie or episode gets `IsPlayed`; a show or a season gets `UnplayedItemCount` and `IsPlayed` once every episode is watched, not counting episodes in Media Trash; every item gets the profile's `MyRating`. The expressions are written once, in `TitleWatchState`, and set on a projection with `Assign` (`ProjectionExtensions`), so each list is one query whose rows carry their own counts (subqueries on the show's seasons and the season's episodes, against the profile's watched states). They used to be added afterwards by three copies of an attach step that read every episode id of every listed show into the server and sent them all back to look up watched states, which made a TV library page take twice as long as a larger movie library; the collection page's copy also counted trashed episodes. The library page lists its titles by naming their types (`MediaCapabilities.IsListedInItsLibrary`, `MediaType IN (…)`) so PostgreSQL uses the `(LibraryId, MediaType)` index instead of reading every episode row to drop it. `SmartListRepository` once had its own copy that skipped seasons and counted trashed episodes, so a finished season never showed its ✓ on Home.

## Libraries a client leaves out

A profile pins and unpins libraries in its sidebar on each device (its nav prefs, `profileDeviceSettingsService.saveNavPrefs`), so the server can't know which to leave out; the client says. Two Home reads take an optional `excludeLibraryIds`: `GET /api/smartlists/{id}/entries` and `GET /api/recommendations/global`. Continue Watching (`GET /api/sync/profiles/{profileId}/continue-watching`) does not: it keeps whatever the profile was watching, in any library.

- **Format:** comma-separated library ids, `?excludeLibraryIds=a,b`. Absent or empty changes nothing, so older clients get what they always did. A token that isn't a GUID is skipped, never a 400 (`QueryGuidList.Parse` in the API).
- **Which library a title is in:** its own `MediaItem.LibraryId`, as the access filters read it. A season and an episode carry their show's library (the scanner creates them in it, and shows are per library), so a Movies & Shows row's show tile, grouped from its episodes, goes with them.
- **Before the limit:** library rows add `LibraryId NOT IN (…)` to the query in `SmartListRepository` (through `SmartListViewer.ExcludedLibraryIds`) before the TV pools and `Take(maxItems)`, so a row fills from the other libraries instead of coming back short. The default rows still read from their sort indexes with a library left out (`PostgresHomeRowIndexTests`).
- **For You** leaves them out when it loads the tiles of the stored rows, not when it builds them: the stored rows are the profile's on every device (`docs/for-you.md`). A row can come back shorter, and one with nothing left is dropped.
- Nothing here is cached per viewer, so nothing carries over between requests. Music libraries are not sidebar entries and are never sent; channel, station, podcast and recording rows ignore the list.

## Access

`SmartListEndpoints.Viewer` turns the caller's claims into a `SmartListViewer`. Library rows apply the library, rating and unrated filters like every other library read. Favorites check the account's and the profile's IPTV playlist allowlists (the `IptvManager.GetClientPlaylistsAsync` rule) and drop inactive playlists, admin-hidden channels, channels that failed their health check and channels outside the playlist's country filter. Music uses `GetMusicAccessFilter()`. Podcasts and recordings are the profile's own.

## Defaults

`SmartListDefaults.All` is the one list of default rows: **Continue Listening** (`ContinueListening`, up to 10 queues) first, then the six original library rows with **Popular Movies & Shows** (movies and shows, `Popular`, 20 titles) after the two recently rows, then Favorite Channels, Recent Recordings, Recently Added Music, New Podcast Episodes and Favorite Radio Stations, in that display order. It seeds the table (`HasData`, so the ids are fixed) and stamps each row's `DefaultKey`. Admins can turn any default off or delete it; **Restore default lists** re-adds only the deleted ones, at the end of the order, with their original ids. Editing a default keeps its key.

A server that already had the rows gets Popular Movies & Shows once, from the migration (`docs/database.md`): it goes after the two recently rows, every row from there down moves one place and an admin's own rows keep their order. Continue Listening arrives the same way, first: every row moves one place down. An admin who removes either keeps it removed. Continue Listening is drawn only for a profile with a queue to continue, so for everyone else the first row on Home is the one that was first before.

`ContinueListening` rows are the viewer's own: `SmartListSourceResolver` reads `MusicResumeManager.GetItemsAsync` for the viewer's profile through the viewer's own library and rating access, and returns entries of kind `MusicResume` (`SmartListEntryVM.MusicResume`, the same item the Music For You row gets). A profile with nothing saved gets an empty row, which no client draws. Nothing in the editor changes for it: one sort (most recent first), no rules, no library.

## Clients

The web row is `components/Home/SmartListRow.tsx`: posters for library titles (open details, songs play; the ✓ or the episodes-left count through `MediaCard`'s `isPlayed` / `unplayedCount`, as in the library grid), 16:9 logo tiles for channels and recordings, square tiles for stations, episodes and albums. Channel, station, episode and recording tiles play in place (`utils/playables.ts`); an album opens the Music page on that album. **A row with no entries is not drawn**, and personal rows show no loading skeleton so an empty one never flashes. A `MusicResume` entry is a square tile (the playlist, smart playlist or mix cover, or the song's album art) that continues the queue in place (`useResumeMusic`). Rows refresh on `ChannelFavoritesUpdated`, `PodcastEpisodesUpdated`, `DvrSessionsUpdated`, `MusicAlbumUpdated`, `MusicResumeUpdated` and `LibraryUpdated` as relevant, and the home page refetches the row list on `SmartListsUpdated`. The home page passes each row the libraries unpinned in the sidebar and draws the rows once the sidebar is in (`docs/frontend-client-ui.md`, Collections follows the sidebar).
