# Local metadata: NFO files and images beside the media

Emby, Jellyfin and Kodi save a title's details in `.nfo` files and its artwork in image files next to the video. Vora can read both. Each library decides, with two settings that are **off by default**, so a library that never turns them on behaves exactly as it did before they existed.

The NFO reader and its test fixtures came from PR #97 (`harikiran-lab`, issue #69). The rules around it (per-library modes, Fix match, serving images in place) are in this doc.

## The two library settings

`MediaLibrary.LocalMetadataMode` (`LocalMetadataMode`: `Off`, `MatchOnly`, `Prefer`) and `MediaLibrary.PreferLocalArtwork`. Both are saved only for Movie and TV libraries (`LibraryType.ReadsLocalMetadata()`, applied by `LocalMetadataSettings`). In the admin they are **Read NFO files** and **Prefer local artwork** on Create Library and Manage Library (`components/Admin/Libraries/LocalFilesFields.tsx`). `PUT /api/libraries/{id}` leaves either one as it is when the request does not include it, so a client that predates them cannot switch them off by saving a library.

| Read NFO files | Which title it is | Where the details come from |
| --- | --- | --- |
| Off | File and folder names, as before | The library's metadata provider |
| For matching only | The NFO's ids (or its title and year) | The library's metadata provider, every field |
| Prefer NFO details | The NFO's ids (or its title and year) | The NFO first; the provider fills in what the NFO leaves out |

**Prefer local artwork** is separate. When on, a poster, backdrop or logo found beside the title is used before the artwork provider's. The provider's images are still fetched and stay in the artwork picker.

The older **Local Assets** entries in the provider dropdowns still work: a library whose metadata provider is `local_metadata` reads only NFO files (as `Prefer` with no provider behind it), and one whose artwork provider is `local_artwork` uses only local images.

## Which NFO belongs to a title

`LocalNfoReader` + `LocalMediaAssets` (`Vora.Plugins/Providers/Local`).

- **Movie:** `<video name>.nfo` first. `movie.nfo` only when the folder holds **one film** (`LocalMediaAssets.HoldsOneFilm`: every non-extra video in the folder has the same name up to its year, so two versions of one film still count as one). A `movie.nfo` in a folder of several films is ignored, otherwise it would name all of them.
- **Show:** `tvshow.nfo` in the show folder. `LocalMediaAssets.ShowDirectory` finds that folder from an episode path: a folder holding `tvshow.nfo` is the show folder; `Season 1`, `Season.01`, `Series 2`, `Specials`, `S01` mean the parent is; any other name (including a flat show folder such as `Season of the Witch` or `Special Ops Lioness (2023)`) is the show folder itself.
- **Season:** `season.nfo` beside the episodes, ignored when its `<seasonnumber>` is a different season.
- **Episode:** the video's own `.nfo` only. A file holding two episodes has two `<episodedetails>` blocks in one NFO; each episode takes the block with its number.

An NFO is **not trusted** (`LocalIdentity.Trusted`) when the year in the file or folder name and the NFO's year differ by more than one, or when an id tag in the name (`{tmdb-603}`, `[tmdbid-603]`, `[imdb-tt…]`) names a different id than the NFO. The tag wins.

`LocalMediaFolders` is the per-pass cache: one directory listing per folder and one parse per NFO. Before parsing it reads the first 512 bytes; a file that does not start with `<` (a release group's text `.nfo`) is skipped with a debug line, not a warning. DTDs are prohibited and files over 4 MB are skipped.

Read from an NFO: title, original title, sort title, plot, tagline, `mpaa` (reduced to the bare rating: `Rated PG-13` and `US:PG-13` both become `PG-13`; anything else is dropped), premiere date or year, runtime, TMDB / IMDb / TVDB ids (`tmdbid`, `uniqueid`, an old-style `<id>tt…</id>`), genres Vora knows, actors, directors, writers, YouTube trailers, a collection that has both a name and a TMDB number, and stinger tags. Actor `<thumb>` values are kept only when they are `http(s)` addresses.

## A match made by hand is never undone

Fix match puts `"Match"` in the title's `LockedFields` (`MediaMatchManager.MatchLock`). Three places honour it, and each checks **before** the NFO is used, because a forced refresh ignores ordinary field locks:

- **Refresh** (`MetadataFetchService.Trusted`): for a title that has the mark, or a locked id field, the NFO is used only when it shares an id with the title and contradicts none. Otherwise it is dropped and the provider is asked by the title's own ids.
- **Episodes and seasons** of such a show: their NFO files are used only when the show's `tvshow.nfo` passes that same test. NFO files written for the wrong show do not rename its episodes.
- **Scan** (`VoraLocalMediaScannerProvider`, in every mode including Off): a new file joins a hand-matched title instead of creating a second one. For a film, when a file already in the library sits in the same folder under the same name and year. For a show, when any known file is in the same show folder. `IMediaIngestionService.FindHandMatchedOwnerAsync` answers it. A show folder that is the library root never qualifies.

When two copies of a show are merged, the kept one inherits the mark (`MediaDedupeRepository.MergeShowIntoAsync`). Saving edited details (`MediaManager.UpdateMediaMetadataAsync`) replaces the other locks with the list the client sends but can neither add nor drop this one, so a client that leaves it out does not undo the match.

**Clear manual match** (`DELETE /api/media/{id}/match`, admin; the menu entry appears only on a title that has the mark) removes it, sets the ids back to whatever tag is in the file name, sets the title and year back to the ones in the file or folder name unless those fields are locked, and queues an ordinary refresh. For a show the episodes are read again too. From then on the NFO, the tag or a title search decides again.

## Removing a title's metadata

**Remove metadata…** (`DELETE /api/media/{id}/metadata`, admin, movies and shows, `MediaMatchManager.RemoveMetadataAsync`) is for a title that should not be matched at all, such as a home video taken for a film.

- **What goes** (`MetadataReset`): the ids, description, tagline, content rating, ratings, poster, backdrop, logo, genres, studios, countries, networks, cast, trailers and the provider artwork list. For a show, the same goes for every season and episode.
- **Names come back from the files:**
  - the title and year from the file or folder name (`ReadNamingAsync`);
  - seasons as `Season N`;
  - episodes from their file names, through the library's episode pattern (`EpisodeFileNames`), or as `Show - S01E02` when the name has no title.
- **What stays:** fields the admin locked, uploaded images (`IsUserUploaded`), watch history, collections, markers and the files.
- **It stays unmatched:** `"Unmatched"` (`MediaMatchManager.UnmatchedLock`) replaces `"Match"` in `LockedFields`.
  - `MetadataFetchService.StaysLocal` then treats the title, its seasons and its episodes like a home video: no provider is asked, NFO ids are ignored, and artwork comes from the folder or a frame of the video.
  - The scan and saving edited details treat the mark like `"Match"` (`IdentityChosenByHand`), so new files join the title and an edit cannot drop the mark.
- **Undo:** the menu offers **Match automatically** in its place. That is Clear manual match, which now lifts either mark. Fix match replaces it with `"Match"`.

## How a refresh uses the NFO

`MetadataFetchService.GetTextMetadataAsync`:

1. Mode `Off`: ask the provider. Nothing on disk is read.
2. Read the NFO through `ILocalMediaReader` and apply the trust rules above.
3. **Adopt its identity** (movies and shows only, never a hand-matched title): if the NFO's ids contradict the title's, the three ids are replaced together, the way Fix match does; if the title has none, they are filled in. With no ids on either side the provider is searched by the NFO's title and year.
4. Ask the provider.
5. `MatchOnly`: return the provider's answer. `Prefer`: `LocalMetadataMerge.Overlay` lays the NFO over it. With no provider answer the NFO is returned alone.

What the overlay takes from the NFO when present: title, original title, sort title, plot, tagline, content rating, runtime, release date, genres, collection, cast. The NFO's trailers are listed first and the provider's are kept. A year on its own does not replace the provider's full date in that year. NFO cast members get the provider's person id and photo, matched by id then by name. The provider keeps status, ids, and everything an NFO cannot say (budget, companies, networks, seasons, upcoming episodes).

## Picking up changes

Units of a scan only cover new files, so two things look at titles already in the library:

- **Every library scan** runs "Reading local metadata…" before "Fetching details…" (`IMetadataManager.TriggerLibraryLocalMetadataSyncAsync`). `LocalMetadataChangeFinder` lists the library once, lists each folder once, and reports a title when one of its NFO files was saved after the title's `LastMetadataRefresh`. Those titles are refreshed **without** overriding locked fields. A library with both settings off is not listed at all. An NFO dated in the future waits until that time has passed; an old NFO copied in with its old date is not noticed (use Refresh metadata on the title).
- **Saving a library with either setting changed** queues `QueueLocalMetadataSync` (resumable, `docs/task-resume.md`). It carries the time it was asked for, and reads every title that has an NFO and was last refreshed before then, so a restart does not redo finished titles.

In `MatchOnly` only movie and show NFO files count; in `Prefer` season and episode ones do too. Episodes of one show are refreshed on one scope so the provider's episode list is fetched once. When a show's ids change, all of its episodes are read again.

The same pass handles images: a title whose slot is not locked and whose local image is not the one in use (new, replaced, or deleted) has its artwork fetched again, then its overlay regenerated.

## Local images are read where they are

Nothing is copied into Vora's data folder except the resized copies in the artwork cache.

- **Names** (`LocalMediaAssets.FindArtwork`): `poster` / `folder` / `cover`, `fanart` / `backdrop` / `background`, `logo` / `clearlogo`, as JPG, JPEG, PNG or WebP. A film's own `<video name>-poster|-fanart|-logo` wins, and the shared names apply only when the folder holds one film. An episode uses `<video name>-thumb` or `<video name>`. A season uses `seasonNN-poster` / `seasonNN` (`season-specials` for season 0) beside its episodes or in the show folder, or the plain poster inside a dedicated season folder; a flat show's poster is never handed to a season.
- **Address:** `MediaArtwork.Url`, `PosterUrl`, `BackgroundUrl` and `LogoUrl` hold `/api/artwork/local/{mediaItemId}/{fileName}?v={last write ticks}` (`LocalArtworkAddress`). No path is stored and there is no new column. Replacing the file changes `v`, so the cache and the browser fetch it again.
- **Serving:** `GET /api/artwork/local/{mediaItemId}/{fileName}` is anonymous like the other artwork routes. `ILocalArtworkLocator` (singleton) finds the title's folders from its files (`IMediaRepository.GetLocalMediaSourceAsync` → `LocalMediaPlace`) and returns a file only when the name is an artwork name **for that title** (`LocalMediaAssets.IsArtworkName`) and the file is in one of those folders. An NFO, a video, a neighbour's poster or a path with a separator is a 404.
- **Thumbnails and overlays:** `ArtworkThumbnailService.LoadSourceAsync` and `PosterOverlayManager.EnsureLocalArtworkAsync` resolve the address through the same locator. The web's `thumbUrl` sends it through `/api/artwork/thumb` like an upload.
- **Choosing** (`MetadataMappingService.ApplyArtworkAsync`): with Prefer local artwork on, a `local_artwork` row beats any vote count; otherwise the best-voted wins as before. In a library that prefers local artwork, the poster that arrives with the provider's details and a show refresh's season posters do not replace a local image that is in use.
- **Picker:** fetching artwork for one title always adds the local images it has, whatever the library setting, so one can be chosen by hand.
- **Backups:** a locked local address restored onto a title with a different id is rewritten to that id (`LocalArtworkAddress.Rehome`).

Other clients must treat the `/api/artwork/local/` prefix exactly like `/api/artwork/custom/`: a server-relative image that can go through `/api/artwork/thumb`.

## Fixed alongside

- Refreshing artwork deleted the images an admin had uploaded for the title (`ReplaceMediaArtworkAsync`). It now replaces only what providers found (`ReplaceProviderMediaArtworkAsync`).
- A template with nothing to draw made the overlay provider return its source path, which was stored as the poster. The poster now keeps its address.
- A film's trailer in an NFO library is attached under the name the NFO gave the film, not the folder's.

## Not read

Ratings (the library's rating providers own those slots), watch history, studios and countries without provider ids, genres Vora does not have, `.bif` files and stream details. NFO and image files are never written or moved.

## Tests

`LocalMetadataImportTests` (modes, trust rules, Fix match, images through the fetch service, the issue #69 and FROM samples), `LocalMetadataScanTests` (scanner), `LocalMetadataChangeFinderTests`, `LocalMetadataMergeTests`, `LocalMetadataSyncTests`, `LocalArtworkSelectionTests`, `LocalArtworkAddressTests`, `PosterOverlaySourceTests`, `MediaMatchManagerTests` (clear), `MediaMetadataRemovalTests`, `LibraryManagerTests`; `LocalMediaSourceTests`, `LocalArtworkThumbnailTests`, `MediaEditsRestoreTests`, `PostgresMetadataRemovalTests` and the Postgres migration test in `Vora.Infrastructure.Tests`; `LocalArtworkEndpointTests` and `MediaMatchEndpointTests` in `Vora.Api.Tests`.
