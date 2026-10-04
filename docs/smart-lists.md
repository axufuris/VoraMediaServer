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

A row whose feature is off is dropped from `GET /api/smartlists/active` for everyone (admins included — the home screen shows what is turned on) and its entries come back empty.

### Options per source

All sources share `Title`, `SortBy`, `MaxItems` (clamped 1–100 by `SmartListManager`), `ShowOnHomepage`, `ShowToFriends` (off = admins only) and the optional yearly window `ActiveStart/EndMonth/Day` (December → January wraps; an impossible date drops the window on save rather than breaking `/active`). The rest of the options reuse `FilterRulesJson` (`SmartListRulesDto`):

- **Library**: `mediaTypes`, `decade`, `genreIds`, `contentRating`, `unwatchedOnly`, plus `LibraryId` to read from one library. Every `SmartListSortBy` applies; `TitleAsc` sorts by sort title.
- **Favorites**: sort `TitleAsc` (name), `DateAddedDesc` (recently favorited) or `Random`. The same channel in two playlists shows once.
- **Podcasts**: `unwatchedOnly` (only episodes the profile hasn't finished) and `days` (published within). Always newest first.
- **Music**: `LibraryId` (one music library) and sort `DateAddedDesc` → recently added, `ReleaseDateDesc` → newest year, `TopRated` → most popular (Last.fm), `TitleAsc` → artist A–Z.
- **Recordings**: `days` (recorded within). Only `Completed` sessions with an output file.

`SmartListManager.Apply` clears `CollectionId` on anything that isn't a library row and `LibraryId` on anything that isn't library or music, so switching a row's source can't leave a stale filter behind.

## Endpoints

- `GET /api/smartlists/active` → `SmartListClientVM[]` (`id`, `title`, `displayOrder`, `source`).
- `GET /api/smartlists/{id}/entries` → `SmartListEntryVM[]`, the endpoint clients render. Each entry has `kind` (`Media`, `Channel`, `Station`, `PodcastEpisode`, `Album`, `Recording`), `id`, `title`, `subtitle`, `imageUrl`, and exactly one payload: `media` (`LibraryItemVM`), `channel` (`IptvChannelVM`), `podcastEpisode` (`PodcastFeedEpisodeVM`), `album` (`AlbumVM`) or `recording` (`IptvRecordingSessionVM`) — the same shapes the Live TV, Radio, Podcasts, Music and DVR screens already use, so playing an entry is the code those screens already have.
- `GET /api/smartlists/{id}/items` → `LibraryItemVM[]`, kept for clients that predate sources. It returns `[]` for a non-library row, so an old client simply hides it.
- Admin (`/api/admin/smartlists`, `AdminOnly`): list, create, update, delete, `PUT /reorder`, `GET /defaults` (`SmartListDefaultVM`: key, title, source, `isPresent`) and `POST /defaults/restore`.

Library entries carry `Type = "Track"` for songs (they were `"Unknown"`), with the artist on `LibraryItemVM.Artist` and the album cover as the poster.

## Access

`SmartListEndpoints.Viewer` turns the caller's claims into a `SmartListViewer`. Library rows apply the library, rating and unrated filters like every other library read. Favorites check the account's and the profile's IPTV playlist allowlists (the `IptvManager.GetClientPlaylistsAsync` rule) and drop inactive playlists, admin-hidden channels, channels that failed their health check and channels outside the playlist's country filter. Music uses `GetMusicAccessFilter()`. Podcasts and recordings are the profile's own.

## Defaults

`SmartListDefaults.All` is the one list of default rows: the six original library rows plus Favorite Channels, Recently Added Music, New Podcast Episodes, Favorite Radio Stations and Recent Recordings. It seeds the table (`HasData`, so the ids are fixed) and stamps each row's `DefaultKey`. Admins can turn any default off or delete it; **Restore default lists** re-adds only the deleted ones, at the end of the order, with their original ids. Editing a default keeps its key.

## Clients

The web row is `components/Home/SmartListRow.tsx`: posters for library titles (open details, songs play), 16:9 logo tiles for channels and recordings, square tiles for stations, episodes and albums. Channel, station, episode and recording tiles play in place (`utils/playables.ts`); an album opens the Music page on that album. **A row with no entries is not drawn**, and personal rows show no loading skeleton so an empty one never flashes. Rows refresh on `ChannelFavoritesUpdated`, `PodcastEpisodesUpdated`, `DvrSessionsUpdated`, `MusicAlbumUpdated` and `LibraryUpdated` as relevant, and the home page refetches the row list on `SmartListsUpdated`.
