# Audio output and track selection

Which version and audio track a play request gets, and how that track reaches the viewer. The path decision as a whole is in [`docs/streaming.md`](streaming.md).

## What each streaming profile is for

- **Direct** (`DirectStreamPreference`) gives the client the best version and track it can play without converting, and converts only when nothing plays as it is.
- **Client Preference** gives the client the best it can handle, or what the viewer picked, and converts when that is what it takes. It still plays directly when that gives the same result.
- **Bitrate** (`BandwidthOptimized`) gives the best version and track that fit the bitrate limit. It goes to a smaller version only when the larger one is over the limit or its video cannot play as it is. It does not prefer small for its own sake.

A lower resolution pick and a bitrate limit apply in every profile.

## What a client sends

The `capabilities` object on `/api/streaming/start`, `/decision` and `/start-extra` (`DeviceCapsDto`):

| Field | Meaning |
|---|---|
| `detectedAudioChannels` | The output layout the device found: 2, 6 or 8. Absent or 0 marks an older client. |
| `pickedAudioChannels` | The viewer's **Audio output** setting. 0 is Automatic. |
| `playsAnyAudioLayout` | The client mixes any layout down by itself. Browsers send `true`. |
| `canSwitchAudioTracks` | The client can select an audio track while it plays the original file. TV apps send `true`. |
| `rendersImageSubtitles` | The client draws PGS, VobSub and DVB subtitles itself, from the original file. Android TV, Fire TV and the desktop app send `true`; absent or `false` burns them in as before. It counts only when the decision is Direct Play (`docs/subtitles.md`). |
| `maxAudioChannels` | The older single value: the lower of detected and picked. The server reads it only when `detectedAudioChannels` is absent, and a client keeps sending it so it still works against an older server. |

The detected value and the pick travel separately because the profile decides how far a pick may go. Folding them on the client is what made a 5.1 pick on a stereo-detected device impossible to honour.

## Limits

`AudioOutputLimits.Resolve` turns those fields into three numbers: **Hard** (the most channels a track may have and still be sent unconverted), **Target** (the layout to aim for) and **Pick**.

| Client and setting | Direct | Client Preference / Bitrate |
|---|---|---|
| TV app, Automatic | hard = target = detected | the same |
| TV app, pick above detected | hard = target = pick | the same |
| TV app, pick below detected | hard = detected, target = pick | hard = target = pick |
| Browser, any setting | hard = 8, target = pick or detected | the same |
| Older client | hard = target = `maxAudioChannels` | the same |

A pick above what was detected is honoured in every profile whenever a track in that layout plays without converting. Under Direct a pick is only ever a preference: it never causes a conversion, in either direction. Under the other two profiles a TV app's lower pick is a real limit, and a 5.1-only file is downmixed. A browser is never limited, because it downmixes by itself and a server-side downmix would buy nothing.

## The rules

In every profile:

- Automatic stays in the profile's audio language when the file has it, otherwise in the default track's language, and off commentary tracks (**Profile preferences** below). What happens to a version or track the viewer picked is under **Picks** below.
- Audio is converted only when the client cannot decode it, it has more channels than the hard limit, or the HLS stream cannot carry it.
- The server never converts just to move between 5.1 and 7.1. Conversion is worth it to make a track playable or to keep surround instead of stereo.
- The video is never re-encoded for the sake of audio when another track avoids it.

By profile:

- **Direct.** If any eligible track plays without converting, nothing is converted, and the largest version that plays that way wins. Among its tracks the closest to the target wins, and surround is kept over stereo when the target is surround.
- **Client Preference.** The largest version wins, converted if it has to be. The track in the wanted layout is used when it plays without converting; otherwise the audio alone is converted to keep surround when the output has it and the file has it.
- **Bitrate.** Audio follows Client Preference. For the version it follows Direct: the largest version whose video plays as it is within the bitrate wins, and a version is converted only when none does. A conversion under this profile is encoded at the ladder bitrate for its height rather than at the limit.

Direct and Bitrate both prefer a version whose video is untouched over re-encoding a larger one. Client Preference is the profile that re-encodes the larger one.

**The screen and the connection** in each profile. The screen (`displayWidth` / `displayHeight`) never makes a version unplayable, in any profile. It only sizes a conversion and decides when a converted larger version gives "the same result" as a smaller one that plays, so even Client Preference plays the 1080p file rather than converting a 4K one for a 1080p screen. The measured connection is a bitrate limit under Client Preference and Bitrate, never under Direct (`docs/streaming.md`).

## Picks

A pick is a version, audio track or subtitle the viewer chose, either for this play (sent with the request) or remembered for the title (below). A pick sent with the request outranks a remembered one of the same kind. `PlaybackWish` turns both into what was picked (edition, height, codec and HDR of a version; language, commentary or not, codec and layout of a track; language and forced/SDH of a subtitle), so a pick also finds its match in another episode or another version.

| Profile | The pick plays without converting | The pick would have to be converted |
|---|---|---|
| **Direct** | It plays. | The best option that plays without converting, in the same language and edition. Only if none does is the pick converted. |
| **Bitrate** | It plays, if it is also within the bitrate. | The same, within the bitrate. |
| **Client Preference** | It plays. | It plays, converted. |

Whether a pick plays is judged only by what was picked: a version by its video, a track by its audio (and by whether it forces the video to be re-encoded for HLS). Whatever was not picked follows the profile's usual rules, so Bitrate still converts automatic audio to keep surround. A burned-in subtitle never counts against a pick. `BestPathDecisionManager.PoolFor` holds the rule; the decision says `pickReplaced` when it played something other than the pick, and the reason says why.

## Remembered choices

What a viewer picks on a title page or while watching is remembered **per profile, per device and per title** (`ProfileTitlePlaybackChoices`, keyed by profile, `X-Vora-Device-Id` and the movie or show; an episode or season counts as its show). Per device, because the same person's TV and phone play different codecs.

- **What is kept:** the picked version's edition, height, codec and HDR; the audio track's language, commentary flag, codec and layout; subtitles off, or a language with its forced and SDH flags. No track ids, so a show's choice carries to every episode.
- **Applied by the server** to every `/start` and `/decision` that does not name that kind itself, so next-episode autoplay, playlists and every client that leaves the choice to the server get it with no extra work.
- **Missing in an episode:** a language or edition the episode does not have is ignored for that kind, and the automatic choice is used (for subtitles too, so forced subtitles still show).
- **API:** `GET /api/streaming/choices/{mediaId}`, `PUT` with `mediaPartId`, `audioTrackId`, `subtitleTrackId` or `subtitlesOff` to remember a pick and `forget: ["Version" | "Audio" | "Subtitles"]` to hand a kind back to Automatic, `DELETE` to forget the title on this device. `mediaId` is the item whose tracks were picked from. `PlaybackChoiceManager` builds the row from the track; a track that is not part of the title is refused.
- **Backups:** `users.playback-choices`, titles matched by identity on restore.

## Profile preferences

Set once per **profile** (not per device: a language does not depend on what the device decodes) on the client's Settings → Playback → **Audio & subtitles**, stored on `UserProfile`, and read by `/start`, `/decision` and `/start-extra` through `TrackPreferences`. Each choice starts on **Automatic**, which keeps the server's behaviour.

| Setting | Automatic | Otherwise |
|---|---|---|
| Audio language (`PreferredAudioLanguage`) | The file's default track's language. | Tracks in that language, then ranked as usual; a file without it falls back to Automatic. |
| Subtitles (`SubtitleMode`) | Smart when the server's **Turn on subtitles when the audio is in another language** is on, else Only forced (a track the file just flags as default stays off). | `Default`, `Smart`, `ForcedOnly`, `Always`, `Off` (`docs/subtitles.md`). |
| Subtitle language (`PreferredSubtitleLanguage`) | The server's `MetadataLanguage`. | That language. |
| Prefer SDH (`PreferHearingImpairedSubtitles`) | Off: an SDH track is a last resort. | SDH first among the tracks in the subtitle language. |

- **Order:** a pick sent with the request, then a remembered choice for the title, then the profile preference, then the file's flags. The language narrows the tracks before the profile ranks them, so Direct and Bitrate still pick the best-playing track in that language.
- **Subtitles follow the audio that plays:** Smart compares the subtitle language with the language of the track the decision will use (preferred, remembered or default), not always the default track's.
- **API:** `GET` and `PUT /api/users/profiles/me/track-preferences` (`TrackPreferenceManager`). It has its own route rather than fields on `playback-preferences`, so an app that saves auto-skip with an older body cannot clear these. Languages are stored as `MetadataLanguageCodes.Normalize` codes (`ja` and `Japanese` both become `jpn`); `null` is Automatic. `GET` also returns `serverLanguage` and `serverSubtitleMode`, so a client can say what Automatic means.

## Converted audio

`AudioOutputLimits.ConversionOf` decides what a conversion produces:

- **Dolby Digital (AC3), at most 5.1,** when the result is surround and the client is an app that lists `ac3`. A device feeding a soundbar or receiver can pass that through; multichannel AAC often comes out as stereo there.
- **AAC** otherwise: for stereo, for a browser, and for an app that does not list `ac3`.

`FFmpegTranscodeService.ConvertedAudioArguments` encodes AC3 5.1 at 640 kbps, AAC surround at 384 kbps and stereo at 192 kbps.

## Scoring

The rules above are weights in `AudioPlayback`, added to the option's penalty by `AudioOutputLimits.FitPenalty` and `BestPathDecisionManager`. What matters is how they relate:

| Weight | Value | Why it has that size |
|---|---|---|
| `LayoutPenaltyPerChannel` | 5 | Orders tracks by distance from the target. At most 30, so less than a remux: a layout that was only detected is not worth repackaging the file. |
| `PickMissedPenalty` | 60 | Added when the track is converted or is not in the picked layout. With the distance it exceeds a remux: an explicit pick is worth repackaging. It applies to converted audio too, so a pick alone never justifies a conversion. |
| `RemuxPenalty` | 50 | Copying streams into HLS. |
| `ConversionPenalty` | 40 | Converting audio, on top of the profile's 100 (or 50,000 under Direct). Kept below the 50 a video conversion costs. |
| `SurroundLostPenalty` | 200 | Stereo when the target is surround. More than an audio conversion costs outside Direct, so surround is kept by converting. |
| `HlsVideoCarriage.ReencodePenalty` | 1,000 | The video is re-encoded only because the stream has to be HLS and the client's HLS player cannot take its codec. More than any audio preference. |
| `ConvertedVideoPenaltyWhenAnotherVersionMayPlayUntouched` | 25,000 | Under Direct and Bitrate, more than the largest resolution penalty, so an untouched smaller version beats a re-encoded larger one. |
| `beyondScreenPenalty` | 1 per line | From the screen's height up to the client box's, so the larger of two versions that both cover the screen wins by hundreds of points. Zero without a screen hint. |

The quality rank of the codec and the default flag only break ties. A version that plays without converting its video is scored at its own height; the HDR downscale setting only lowers the height of a version that is being converted.

## How the chosen track reaches the viewer

- **The client switches tracks.** The decision is `DirectPlay` and both responses carry `audioTrackPosition`: the zero-based position of the track among the file's audio streams, in stream order. The app selects that track in its player. `subtitleTrackPosition` does the same for the subtitle, counting embedded subtitle streams only. The details payload carries the same position on each track (`embeddedPosition` on audio tracks and on embedded subtitle tracks), so a player can switch mid-play.
- **The client cannot switch tracks** (browsers, older apps). The original file is served only when the wanted track is both flagged default and the first audio stream. Otherwise the decision is `Remux`: FFmpeg copies the video and the one wanted track into HLS, and `TargetContainer` is `hls`.

The "default and first" test exists because players disagree. Measured in Edge: an MKV always starts on the first audio stream and ignores the default flag, while an MP4 follows the flag. Measured again with an MP4 whose second audio track is the flagged one: Chrome plays that track, Firefox plays the first. So the flag alone can't be trusted even for MP4. `StreamRepository` therefore loads audio tracks in `StreamIndex` order, and `TrackStreamInfoDto.StreamIndex` carries it into the decision.

HLS MPEG-TS carries only AAC, MP3, AC3 and EAC3 (`AudioPlayback.CanCopyToHls`, shared with FFmpeg). Any other codec in an HLS session is converted and reported as converted.

## Video the HLS player cannot take

An HLS session copies the video only when the client's HLS player can take that codec out of MPEG-TS. The client reports the list as `hlsVideoCodecs`. `HlsVideoCarriage.For` falls back to H.264 alone for a browser that sends none, and to H.264, HEVC and MPEG-2 for an app. When the stream has to be HLS (the audio is converted, or the file is repackaged) and the codec is not on the list, the video is re-encoded. `ReencodePenalty` keeps that from happening when another track plays without HLS.

- **The web** reports `['h264', 'hevc']` when Media Source takes HEVC (`detectHlsVideoCodecs`). hls.js plays every HLS stream when Media Source exists; it reads HEVC from MPEG-TS and repackages it for Media Source. So a browser that plays HEVC gets the video copied while only the audio is converted, or the file is repackaged for a track switch.
- **Without Media Source** (older iPhones) the browser's own HLS player is used, which plays HEVC only from fMP4, so the list is H.264 alone and HEVC is re-encoded whenever the stream is HLS.
- **AV1 and VP9** are on no default list: neither hls.js nor ExoPlayer reads them from MPEG-TS, so they are re-encoded rather than copied into a stream that would not play.

The rule used to apply only to HEVC in a browser, only when audio was converted, and never to a repackaged stream. A repackaged HEVC file went to Safari as HEVC in MPEG-TS.

## The web client

- `hardwareScanner.ts` reports `detectedAudioChannels` from `AudioContext.destination.maxChannelCount`, `pickedAudioChannels` from the saved `maxAudioChannels` preference, `playsAnyAudioLayout: true`, `canSwitchAudioTracks: false` and `hlsVideoCodecs`.
- **Vora Desktop** plays video in mpv and sends the desktop profile instead (`utils/desktopCapabilities.ts`): mpv's codecs and containers and the output layout from the app, every HDR format, 12-bit, `rendersImageSubtitles` and `canSwitchAudioTracks: true`, and `hlsVideoCodecs` h264, hevc and mpeg2video. The viewer's Quality and Audio output picks apply as they do in a browser. `playbackCapabilities()` picks the profile for `/start`, `/decision`, `/start-extra` and the device report, and `usePlaybackCapabilities` for the chips.
- The details page's resolution, audio and subtitle chips are menus (`TrackChoiceChips`, `HeroChoiceChip`), Plex style: **Automatic** plus each version or track, with what it would do here (Direct Play or Transcode). `useTitleTrackChoices` shows a local guess (`localTrackGuess`) at once, then asks `POST /api/streaming/decision` with no tracks named, so the server applies what was remembered and answers with the version, audio track and subtitle it will play. A pick is saved with `PUT /choices` and the server asked again; if it cannot be saved it still applies to this play.
- The audio chip reads like its menu line, language first: "Telugu · EAC3 5.1" (`audioChipLabel`), so a file with several dubs says which one will play. The subtitle chip names the language the same way ("CC English"). The language is left out when the file does not set one (`und`).
- Track languages arrive in mixed forms. The scanner stores `eng` as the name "English" and only capitalises every other code ("Tel", "Jpn"); sidecar subtitles carry the code from their file name ("en"). `languageName` in `utils/trackLabels.ts` turns any of them into a name, and keeps a spelled-out name as written, because `Intl` would otherwise lower-case "English" to "english".
- When the server swapped a pick for one that plays (`pickReplaced`), a line under the chips says so.
- Play sends the version on screen, the subtitle (a burned-in one by id, a text one is drawn by the player), and an audio track id only when a pick could not be saved.
- A version, track or subtitle changed in the player is remembered the same way, as long as it is not an extra, Live TV or a recording.

## The matrix test

`tests/Vora.Application.Tests/Streaming/Matrix/` runs the real `BestPathDecisionManager` over every combination of file, client, setting, profile and situation, and checks each outcome against the rules above.

| File | Holds |
|---|---|
| `MatrixCatalog.cs` | Clients (with the codecs their HLS player takes), audio layouts, video files, situations (bitrate limits, subtitles, HDR downscale). |
| `MatrixRunner.cs` | Builds the request the way a client would, including a track or version picked by hand and a profile audio language, and records what the viewer would hear. Margins count only the options a pick allows (`StreamOptionLogDto.Considered`). |
| `MatrixRules.cs` | The rules, written from what a client can play rather than from the scoring. |
| `StreamDecisionMatrixTests.cs` | One test per rule; a failure lists the combinations that break it. |

The normal run covers about 314,000 combinations in a few seconds, including a profile audio language the file has and one it lacks. Set `VORA_STREAM_MATRIX=full` to run every one-, two- and three-track mix of 14 codec and layout types: more than 12 million combinations in a few minutes. **Run the full set before changing a weight or a rule**, because the hand-picked set has missed real problems that the full set found.

The last rule fails when two different kinds of outcome are within ten points of each other. It is there to catch a weight change that leaves a decision to chance.

Three browser clients send a screen hint (a 1440p HDR screen, and 1080p screens with and without HEVC), and two situations have a GPU that keeps HDR. Their rules: a conversion is never larger than the screen, HDR never reaches a client that cannot show it (converted or not), and a conversion keeps HDR10 when the GPU, the player and the stream allow it. The existing rules already check that the screen never causes a conversion, because their "plays as it is" uses only the client's limit.

## Known limits

- The matrix proves the decision, not playback on a device.
- Edge, Chrome and Firefox were measured for which track a browser starts on (Firefox ignores an MP4's default flag); Safari was not. The "default and first" test is safe in all of them, at the cost of a remux for an MP4 whose flagged track isn't first.
- A TV app's hard limit is what it detected. The server does not assume the device can downmix.
- Per-track audio bitrates are not stored, so the bitrate limit is checked against the file's overall bitrate.
