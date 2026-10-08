# Unused files

Vora names most of what it writes under the data folder after a database id. When a row goes away — an item or library deleted, or the database wiped and the server reinstalled with the data folder kept — the file stays. A few caches tidy themselves (the subtitle cache after a scan, the transcode and timeshift janitors, the 512 MB resized-artwork cap, log retention); nothing else ever compared the folders against the database. **System Settings → Storage → Unused files** does.

## Using it

- **Scan for unused files** (`GET /api/admin/maintenance/unused-files`, admin only) lists, per kind of file, how many files nothing uses, their size, the folder and a few example names. Nothing is deleted.
- **Remove N files** asks for confirmation, then queues the **Remove Unused Files** task (`POST /api/admin/maintenance/unused-files/remove` → `ITaskQueueManager.QueueUnusedFileRemoval`, resource key `unused-files`, restored after a restart like every other task — `docs/task-resume.md`). The task scans again and deletes what is unused at that moment, so a file put to use since the preview is kept.
- There is no schedule. A weekly run would need a setting, and a new `ServerSetting` column means re-issuing the combined migration (`docs/database.md`).

`UnusedFileManager` (`Vora.Application/Maintenance`) does both; `IStorageReferenceRepository` (`StorageReferenceRepository`) answers what the database still points to.

## What counts as unused

| Kind | Folder | Unused when |
| --- | --- | --- |
| `Artwork` | `StoragePaths:CustomArtwork`, top-level files only | no text in the database contains `/api/artwork/custom/{name}` or `{folder}/{name}` |
| `ProfilePictures` | `StoragePaths:UserImages`, top-level | no text contains `/api/users/images/custom/{name}` or `{folder}/{name}` |
| `ScrubThumbnails` | `IVideoThumbnailStorageService.RootDirectory` | `{shard}/{itemId:N}` whose item is gone, or `{itemId:N}/{partId:N}` whose part is gone |
| `DownloadedSubtitles` | `SubtitleStorePath.Resolve` | its path is no track's `ExternalFilePath` and its name is no track's id |
| `OriginalArtworkCache` | `StoragePaths:OriginalArtworkCache` | its name is not `PosterOverlayManager.OriginalArtworkCacheFileName` of any item's `OriginalPosterUrl` |
| `SubtitleCache` | `{transcode dir}/subcache` | `{partId}_{trackId}_{fp}.vtt` / `.failed` whose part is gone |
| `RemovedPlugins` | `StoragePaths:Plugins` | `*.deleted` (what an uninstall leaves; only `*.dll` loads) |
| `UnfinishedFiles` | several | `imagecache/**/*.tmp`, backup `*.tmp`, `fp_*.wav` in the fingerprint folder, `subcache/*.tmp` — older than a day |
| `Recordings` | `DvrStoragePath` or `StoragePaths:IptvDvr` | a `*_yyyyMMdd_HHmmss.ts/.mp4` whose path (either extension) is no session's `OutputFilePath` — **reported, never removed** |

Artwork and profile pictures are referenced by URL from many places — media items, albums, artists, collections, playlists, generated mixes, profiles, settings JSON, saved task recipes — and a hand-written list of columns would delete a file the day someone adds a column. So `FindReferencedNamesAsync` reads every `text`, `varchar`, `char`, `json`, `jsonb` and text-array column of every table in the current schema (`information_schema`), and for each runs `regexp_matches(col::text, '(prefix|…)([A-Za-z0-9_.%/+=~-]+)', 'g')`, filtered by `LIKE ANY` so only rows mentioning a prefix are searched. Prefixes are escaped for both. On QA (330 such columns, 29,580 artwork references) the whole pass takes about 3 seconds. A captured name is also kept URL-decoded and cut at its first `/`, so `x.jpg?v=2`, `x%20y.jpg` and `x.jpg/…` all keep their file.

## Safety rules

- Only names Vora itself writes are considered: top-level artwork named `media_…`, `coll_…`, `playlist_…`, `music_…` or `{guid}_overlay_…`, pictures named `profile_…`, `{shard}/{itemId:N}/` for thumbnails and downloaded subtitles, the cache naming for the subtitle cache. Anything else in those folders, dotfiles and unknown subfolders (`imagecache/` included) is left alone.
- A downloaded-subtitles or original-artwork folder that contains another storage folder (say `StoragePaths:Subtitles` pointed at the data folder itself) is not swept at all (`HoldsOtherStorage`) — those two kinds have no name pattern strict enough to trust in a shared folder.
- Anything written in the last hour (`UnusedFileManager.RecentlyWritten`) is skipped — a file saved just before its database row, or a recording in progress. A thumbnail folder counts as recent if any file in it is.
- Media Trash items still have their rows, so their files are kept.
- Removing artwork also removes its resized copies (`IArtworkThumbnailService.RemoveThumbnailsForSource`). Emptied thumbnail and subtitle folders are pruned.
- A file only an old backup points to is unused as far as the database knows. The confirmation says so: restore the backup first to keep it.
- A folder Vora can't read is logged and that kind stops where the error happened; the other kinds carry on.

## Tests

`UnusedFileManagerTests` builds every folder in a temp directory and checks each kind, the recent-file and dotfile rules, that a scan deletes nothing, and that recordings are never removed. `PostgresStorageReferenceTests` checks the column search finds references in plain text, `text[]` and JSON columns and cuts query strings. `StorageReferenceRepositoryTests` covers the regex and `LIKE` escaping, and `OriginalArtworkCacheFileNameTests` the cache naming shared with the overlay code. `StorageTab.test.tsx` covers the admin card.
