# Black bars inside the picture

Many files carry black bars inside the frame itself: a 2:1 or 2.39:1 film encoded into a 16:9 frame (bars above and below), or a 4:3 show inside 16:9 (bars at the sides). The browser's `object-contain` shows the whole frame, so those bars stay. On a window wider than 16:9 the browser then adds its own bars at the sides as well, and the picture ends up smaller than the window on all four sides. Plex shows the same files filling the window.

Vora finds where the picture sits in each file once, stores it, and the player fits **the picture** to the window instead of the frame.

## What is stored

`MediaVideoTrack.CropTop` / `CropRight` / `CropBottom` / `CropLeft` hold the width of each bar in pixels of the coded frame (the same pixels as `Width` / `Height`).

- **All four null:** nobody has looked yet.
- **All four zero:** someone looked and there are no bars.

`PictureCrop` (`Vora.Application/Analysis`) is the value type. It covers reading the columns (`Of`), writing them (`ApplyTo`) and clearing them (`Forget`).

The details view model carries the four values on `MediaDetailsPartVideoTrackVM`, and the web reads them as `VideoTrack.cropTop` and so on.

The columns were added by the folded `ChangesSinceInitial` migration (`docs/database.md`).

## Finding the bars

`IPictureAreaDetector` is implemented by `FFmpegPictureAreaDetector`.

It reads 10 samples, spread evenly between 8% and 92% of the duration, so opening titles and end credits don't count. Each sample is one FFmpeg run that decodes only keyframes:

```
-hide_banner -nostats -skip_frame nokey -ss {t} -i {file} -map 0:{StreamIndex} -frames:v 5
-vf cropdetect=limit=0.094:round=2:reset=0 -an -sn -dn -f null -
```

The last `crop=w:h:x:y` line of a run is that sample's box. Each run may take up to 60 s (`WaitForExitWithTimeoutAsync`).

- The stream is mapped by its absolute index, so a cover image stored in the file is never measured (CLAUDE.md, "A cover image is not a video track").
- The detector returns `null` when the file is missing or fewer than 3 samples ran. `null` means "try again later", and nothing is saved.

`PictureCrop.FromBoxes` turns the samples into one crop:

- **The boxes are joined, not averaged.** One bright scene that reaches the edge of the frame proves there is no bar there, however many dark scenes suggest one. A film that opens up for some scenes (IMAX ratios) therefore comes out as no bars, which is the safe answer.
- A box smaller than a quarter of the frame in either direction is discarded. These come from black frames and fades.
- If fewer than 3 boxes remain, the result is "no bars".
- A bar no wider than 1% of the frame, or 4 px, counts as no bar. Encoders often leave a dark line of a pixel or two at the edge.

## When it runs

`PictureAreaManager` (`IPictureAreaManager`) has two methods:

- `DetectForItemAsync` reads every part of one title.
- `DetectForLibraryAsync` reads every movie and episode in a library that isn't missing.

Both read only parts whose main video track (`CoverImageTracks.MainVideoTrack`) is still unchecked. A pass over a library that has already been read is one query and no FFmpeg. Progress shows as `Finding black bars (n/N)`. A sample that fails is logged and the pass moves on.

Two tasks run them, named **Find Black Bars: …**:

- `QueueDetectMediaItemPictureArea` (dedupe `picture-area:item:{id}`)
- `QueueDetectLibraryPictureArea` (dedupe `picture-area:library:{id}`, re-runs if asked while running)

Both share the resource key `picture-area`, so only one FFmpeg reader runs at a time. A library pass drops the pending item tasks for its library. An item request is skipped while its library's pass is pending. Both tasks save a recipe (`docs/task-resume.md`).

Detection is queued:

- after a new file's analysis (`QueueScanNewFile`) and after a title's **Analyze**;
- at the end of every library analysis for a library that holds video, so the nightly schedule and the library **Analyze** button cover a server that existed before this;
- when a movie or episode whose main track is unchecked is opened (`MediaManager.GetMediaItemAsync`). The player on that first open still shows the whole frame; the next open uses the stored crop.

## Keeping it

`SyncMediaTracksAsync` keeps the crop when a file is scanned again, unless `PictureCrop.StreamChanged` sees a different width, height, codec or bitrate. In that case the file at that path was re-encoded, the crop is forgotten, and it is measured again.

## The web player

The layout code is split across three places:

- `utils/pictureLayout.ts`:
  - `cropOf(track)` turns the stored pixels into fractions of the frame, or `NoCrop`.
  - `pictureLayout(viewport, frame, crop, fit)` works out where the `<video>` goes.
  - `sameLayout` compares two layouts to within a pixel.
- `components/Player/Controls/usePictureLayout.ts` measures the player with a `ResizeObserver` (window `resize` where there is none). It reads the frame from the video's `videoWidth` / `videoHeight` on `loadedmetadata` and `resize`, so a transcode at another size still lines up.
- `GlobalVideoPlayer` positions the `<video>` absolutely at that layout and clips the player with `overflow-hidden`, which cuts the bars off at the window's edge.

There are two modes:

- **Fit** (the default) shows the whole picture as large as the window allows. On a window shaped like the picture there are no bars at all. On a window of another shape, the bars that remain are the window's, not the file's. A 2:1 film in a 16:9 window therefore looks as it did before.
- **Fill** covers the window and trims whatever doesn't fit, usually the sides.

`PictureFitButton` (`Controls/PlayerButtons.tsx`) sits beside fullscreen. It shows only when Fit and Fill would differ. The choice is remembered per browser in `player_picture_fit` (`fit` / `fill`, see `docs/auth-and-devices.md`).

With no crop and Fit, the player keeps the old `object-contain` classes and nothing changes. The mini player and the up-next card always show the whole frame.

The controller leaves the crop out (`pictureCrop` is `NoCrop`) while a subtitle is burned in (`subtitleStrategy === 'BurnIn'`): an image subtitle can sit in the bar, and cropping would cut it off.

## Subtitles over an enlarged picture

The browser places text cues against the `<video>` box. That box now runs past the window, so the old placement (`SubtitleCueLine`, 3 lines up) would put the text partly under the bottom edge, and long lines would run off the sides.

`cueAreaOf(layout, viewport)` (`utils/subtitleCuePlacement.ts`) fixes this:

- It **lifts** cues by the lines hidden below the window. A line is `SubtitleLineShare`, 5.75% of the element's smaller side: Chromium sizes cue text at 5% of that side, and its line box is about 1.15 times the text.
- It **narrows** cues (`size`) to the widest centred band that is on screen.

`WebVideoEngine` remembers the cues it moved (`cuesToMove`: only cues with no line of their own) and places them again on every layout change (`placeSubtitles`).

The placement stays snapped to lines (`snapToLines`). Chromium has no `lineAlign` / `positionAlign`, and it shifts a cue placed by percentage by its own size, which put narrowed cues off centre. Snapped cues stay centred. This was checked by screenshots in Chromium.

## Not covered

- Live TV (no stored crop for a channel).
- Vora Desktop's mpv player, which gets the crop with the mpv engine (`docs/clients/desktop/04-player.md`).

## Tests

- `PictureCropTests`, `PictureAreaManagerTests` (Application)
- `FFmpegPictureAreaDetectorTests`, which measures letterboxed, pillarboxed and full frames with real FFmpeg and is skipped without it
- `PictureAreaRepositoryTests`
- `MediaDetailsPartInfoTests`
- the Postgres migration test for the folded migration
- `TaskRecipeRoundTripTests`
- web: `pictureLayout.test.ts`, `usePictureLayout.test.tsx`, `subtitleCuePlacement.test.ts`
