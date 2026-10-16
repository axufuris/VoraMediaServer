# Music library scanning

How a scanned music file finds its album and its track, how a scan repairs files an older scan put in the wrong place, and how the duplicates page scores a song's files. The domain model is in [`music-and-audio.md`](music-and-audio.md).

## How a file finds its album

`VoraLocalMediaScannerProvider.IngestMusicFilesAsync` reads each file's tags with TagLib, groups the files by album artist, then by album tag (ignoring case), then by **album folder** (`MusicAlbumFolders`).

- **Album folder.** The file's folder, or the folder above it when the file sits in a disc folder (`CD1`, `CD 2`, `Disc 1`, `disc_02`, `Disk 3 - Bonus`; the pattern is `^(?:cd|disc|disk)[\s._-]*\d+\b`, ignoring case).
- **One album tag in several folders.** These stay **one album** when every folder holds its own discs: every file has a disc number above 0, and no disc number appears in two folders (`The Beatles (Disc 1)` / `The Beatles (Disc 2)`). Otherwise there is **one album per folder**. That is how Weezer's seven albums called "Weezer" stay apart.

`IMediaIngestionService.EnsureAlbumAsync(…, albumFolders)` → `MediaIngestionService.FindAlbumAsync` picks the album from the albums that share the artist and title (ignoring case):

1. The album that holds a file in one of the folders.
2. Otherwise, an album with **no file still on disk**. That covers an album whose files were all removed, and a folder that was renamed or moved: the folder watcher removes deleted files only after a 5-second wait, so the new paths usually arrive first. The new files then join the old album, and their tracks keep their plays and likes.
3. Otherwise, a new album.

Callers that pass no folders get the old rule: the exact-case title, else the first album with that title. An existing album also gets a missing `Year` from the tags, unless `Year` is locked.

An artist can therefore have several albums of one title, so `IX_Albums_ArtistId_Title` is a plain index, not a unique one. It used to be unique. The first scan after the change took Weezer's six other self-titled albums off the one album they had been merged into, and could not create them again: the database refused each new "Weezer" album.

**One album the database refuses costs only that album.** `IngestMusicFilesAsync` catches a failure per album, logs it, and clears the change tracker (`ReleaseTrackedEntitiesAsync`), as it does after a failed track. Before, an album failure abandoned the rest of that artist. The rejected insert also stayed tracked, so later saves in the same scan tried it again and failed too.

## How a file finds its track

`EnsureTrackAsync` looks at the album's tracks with the same track and disc number and takes:

1. The one whose `Title` or `OriginalTitle` is the same song by `TrackTitles.Same`. That comparison uses Unicode FormKC and ignores case and punctuation; apostrophes are dropped, so `Don't`, `Don’t` and `Dont` match.
2. Otherwise, the one with the same ISRC.
3. Otherwise, a new track.

A second file of the same song becomes another part of the track. That is a real duplicate, and the duplicates page shows it. A different song with the same number is its own track.

The match used to be album title + track number alone, so a deluxe edition's acoustic version, or a second album with the same title, stacked onto another song as a "duplicate".

## Repairing what older scans stacked

`SeparateMisplacedMusicFilesAsync` runs at the start of every music library scan (`ProcessMusicDirectoriesAsync`). It only looks at files that are already in the library, inside the folders being scanned, and still on disk (`IMediaIngestionService.GetMusicFilePlacementsAsync` → `MusicRepository.GetMusicFilePlacementsAsync`).

1. **Tracks first.** For a track with several files, it reads each file's tag title (the file name when the title tag is empty) and ISRC. When exactly one group of titles is the track's own (`Title` or `OriginalTitle`), the other files are taken off. The track's ISRC is then set to its own files' (`SetTrackIsrcAsync`), because the old ingest gave a track the ISRC of whichever file it saw last. If the stacked file's ISRC were left on the track, `EnsureTrackAsync` would match that file straight back to it on the ISRC; that is what kept True Blue's *White Heat* on *Ain't No Big Deal*. The track is left alone when:
   - no group matches, or more than one does (for example a title edited by hand);
   - any of its files can't be read.
2. **Then albums.** It groups each album's remaining files by album folder, using the disc rule above. The folder with the most files keeps the album (ties go to the first folder path in ordinal order), and the files in the other folders are taken off.
3. **Art and year.** A folder can keep the album even though one of its own files was taken off in step 1. When that happens, the album's cover, background, disc art, year and MusicBrainz id may have come from another folder's file. `ForgetAlbumArtAndYearAsync` clears the unlocked ones and queues `QueueRefreshAlbumArtwork`.

Each file taken off goes through `RemoveMediaItemByPathAsync`, which removes only that file and removes the track only when it was the track's only file. The file is also dropped from the scan's known files, so the same scan adds it again where it belongs. **A track whose only file was in the wrong folder comes back as a new track**, so its plays and likes don't carry over. Tracks that had files stacked on them keep theirs.

Tracks go first because doing albums first sorts the stacked files by folder while they still carry another song's title. On a Weezer library, the Blue Album files that were stacked on Teal Album tracks would then be moved together with their wrong titles.

## Duplicate scores

The duplicates page scores each of a song's files by the file's own format: `MediaDedupeManager.FileCodec(part, track)` → `AudioFileCodecs.Of(filePath)`. `Track.AudioCodec` comes from whichever file was scanned last, so reading it made an MP3 stacked with a FLAC score as lossless. An `.m4a` still takes `alac` from the track, because the extension can't tell ALAC from AAC.

Bitrate comes from the first of these that is known:

1. The part's overall bitrate.
2. File size × 8 ÷ the track's duration.
3. The track's bitrate.

`AudioFileCodecs.Of` is also how the scanner records a track's codec: it turns TagLib's prose description into a short token that fits `varchar(32)`.

## Where to look

| Want to change | Start here |
| --- | --- |
| Album folder / disc folder rules | `Vora.Plugins/Providers/Local/MusicAlbumFolders.cs` |
| What counts as the same song title | `Vora.Plugins/Providers/Local/TrackTitles.cs` |
| Album / track lookup | `MediaIngestionService.FindAlbumAsync`, `EnsureTrackAsync` |
| The repair pass | `VoraLocalMediaScannerProvider.SeparateMisplacedMusicFilesAsync` |
| Codec from a file | `Vora.Plugins/Providers/Local/AudioFileCodecs.cs` |
| Tests | `MusicIdentityTests`, `MusicLibraryRepairTests` (real tagged WAV files), `MediaIngestionServiceTests` |
