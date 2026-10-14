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

- Automatic stays in the default track's language and off commentary tracks. A track the viewer picked by hand is always the one played, converted if it has to be.
- Audio is converted only when the client cannot decode it, it has more channels than the hard limit, or the HLS stream cannot carry it.
- The server never converts just to move between 5.1 and 7.1. Conversion is worth it to make a track playable or to keep surround instead of stereo.
- The video is never re-encoded for the sake of audio when another track avoids it.

By profile:

- **Direct.** If any eligible track plays without converting, nothing is converted, and the largest version that plays that way wins. Among its tracks the closest to the target wins, and surround is kept over stereo when the target is surround.
- **Client Preference.** The largest version wins, converted if it has to be. The track in the wanted layout is used when it plays without converting; otherwise the audio alone is converted to keep surround when the output has it and the file has it.
- **Bitrate.** Audio follows Client Preference. For the version it follows Direct: the largest version whose video plays as it is within the bitrate wins, and a version is converted only when none does. A conversion under this profile is encoded at the ladder bitrate for its height rather than at the limit.

Direct and Bitrate both prefer a version whose video is untouched over re-encoding a larger one. Client Preference is the profile that re-encodes the larger one.

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
| `VideoReencodedForAudioPenalty` | 1,000 | The video is re-encoded only because the audio is converted. More than any audio preference. |
| `ConvertedVideoPenaltyWhenAnotherVersionMayPlayUntouched` | 25,000 | Under Direct and Bitrate, more than the largest resolution penalty, so an untouched smaller version beats a re-encoded larger one. |

The quality rank of the codec and the default flag only break ties. A version that plays without converting its video is scored at its own height; the HDR downscale setting only lowers the height of a version that is being converted.

## How the chosen track reaches the viewer

- **The client switches tracks.** The decision is `DirectPlay` and both responses carry `audioTrackPosition`: the zero-based position of the track among the file's audio streams, in stream order. The app selects that track in its player.
- **The client cannot switch tracks** (browsers, older apps). The original file is served only when the wanted track is both flagged default and the first audio stream. Otherwise the decision is `Remux`: FFmpeg copies the video and the one wanted track into HLS, and `TargetContainer` is `hls`.

The "default and first" test exists because players disagree. Measured in Edge: an MKV always starts on the first audio stream and ignores the default flag, while an MP4 follows the flag. `StreamRepository` therefore loads audio tracks in `StreamIndex` order, and `TrackStreamInfoDto.StreamIndex` carries it into the decision.

HLS MPEG-TS carries only AAC, MP3, AC3 and EAC3 (`AudioPlayback.CanCopyToHls`, shared with FFmpeg). Any other codec in an HLS session is converted and reported as converted.

## HEVC in a browser

`BestPathDecisionManager` re-encodes HEVC video for a browser whenever the audio has to be converted because the client cannot decode it or it has too many channels. That rule predates this work and is unchanged. `VideoReencodedForAudioPenalty` keeps it from firing when another track avoids it.

Audio that is converted only because HLS cannot carry it does **not** trigger the rule. That matches what the server has always done for a repackaged stream. Removing the rule needs HEVC delivered in fragmented MP4 segments, or proof on Safari that MPEG-TS works there.

## The web client

- `hardwareScanner.ts` reports `detectedAudioChannels` from `AudioContext.destination.maxChannelCount`, `pickedAudioChannels` from the saved `maxAudioChannels` preference, `playsAnyAudioLayout: true` and `canSwitchAudioTracks: false`.
- The details page asks `POST /api/streaming/decision` (`useServerPlaybackChoice`) and shows the version and audio track the server answers with. A local pick (`pickBestAudioTrack`) shows until the answer arrives and stays if the server cannot answer.
- Play sends the version on screen, and an audio track id only when the viewer picked one by hand. Asking again after a version or burned-in subtitle change keeps the shown track honest.

## The matrix test

`tests/Vora.Application.Tests/Streaming/Matrix/` runs the real `BestPathDecisionManager` over every combination of file, client, setting, profile and situation, and checks each outcome against the rules above.

| File | Holds |
|---|---|
| `MatrixCatalog.cs` | Clients, audio layouts, video files, situations (bitrate limits, subtitles, HDR downscale). |
| `MatrixRunner.cs` | Builds the request the way a client would and records what the viewer would hear. |
| `MatrixRules.cs` | The rules, written from what a client can play rather than from the scoring. |
| `StreamDecisionMatrixTests.cs` | One test per rule; a failure lists the combinations that break it. |

The normal run covers about 206,000 combinations in a few seconds. Set `VORA_STREAM_MATRIX=full` to run every one-, two- and three-track mix of 14 codec and layout types: about 8.4 million combinations in a couple of minutes. **Run the full set before changing a weight or a rule**, because the hand-picked set has missed real problems that the full set found.

The last rule fails when two different kinds of outcome are within ten points of each other. It is there to catch a weight change that leaves a decision to chance.

## Known limits

- The matrix proves the decision, not playback on a device.
- A repackaged stream is cut on the source's keyframes while the playlist promises even segments.
- Only Edge was measured for which track a browser starts on. Firefox and Safari are assumed to behave the same, and the "default and first" test is safe either way.
- A TV app's hard limit is what it detected. The server does not assume the device can downmix.
- Per-track audio bitrates are not stored, so the bitrate limit is checked against the file's overall bitrate.
