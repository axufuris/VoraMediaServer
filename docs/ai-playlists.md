# AI playlists (music)

Playlists made from a profile's listening with OpenAI: a weekly **Made for you by AI** row, **Bridges** between two of a profile's sounds, **Blends** between two profiles, and a **Make me a playlist for…** box. Built so cost stays small on very large libraries.

## The rule that keeps it cheap and honest

**Songs only ever come from the library.** They are found by vector search over the library's own embeddings, filtered by the viewer's parental controls, so nothing invented or unavailable can appear. The chat model describes songs once, names themes, reads requests, and — only for a request that describes a progression — puts the songs already found in order; it never adds one.

| Step | Cost |
| --- | --- |
| Describe each song once (gpt-4o-mini, batches of 40) | ~$0.80 per 24,000 songs, once |
| Embed each song (`text-embedding-3-small`, ~80 tokens) | ~$0.04 per 24,000 songs; again only when its description changes |
| A profile's taste (mean of the vectors of what it plays) | database only |
| Weekly themes and names | one small chat call per profile per week |
| A request | one embedding plus one small chat call |
| Ordering a request that builds or winds down | one more small chat call (~$0.0005 for 45 songs) |

Every call goes through `IOpenAiClient` (`CompleteJsonAsync` / `EmbedAsync`): the OpenAI plugin's key, its monthly token limit, and a row in AI Usage & Stats under plugin id `openai_music_playlists`.

## Switches

- **Server** (`ServerSetting`, admin **Features → For You**): `EnableAiMusicPlaylists` (default **off**), `EnableAiPlaylistRequests` (default on, meaningful only with the first), `AiPlaylistRequestsPerDay` (default 10). Turning the first on queues the library's embeddings at once.
- **Derived flags** (`FeatureFlagsVM`): `AiPlaylists` = toggle && For You && an OpenAI key; `AiPlaylistRequests` = that && the requests toggle. Clients gate on these.
- **Profile** (`UserProfile.AiMusicPlaylistsEnabled`, default on; `PUT /api/users/profiles/{id}/ai-playlists`, account owner only): off means nothing about the profile's listening is sent to OpenAI, it gets no AI playlists or requests, and no one can Blend with it.

## Song profiles and embeddings

Titles, artists and an album genre (119 broad values on QA) say nothing about how a song feels, so a mood request matched on title words — "Housekeeping Knows" for a house party. Each song therefore gets a **profile**, stored on the song (`Track.Moods`, `Energy` low/medium/high, `Themes`, `GoodFor`, `IsInstrumental`, `ProfiledAt`): `TrackProfileService` sends batches of 40 songs (title, artist, album, year, genre, the artist's Last.fm tags) to the chat model, six batches at a time, each in its own scope. Only songs the answer covers are stamped; a failed call stops the run and the rest are described on the next one. **Moods come from a fixed list of 22** (`SongMoods.All`: happy, upbeat, euphoric … rebellious, epic). The song prompt, the request prompt and the weekly prompt all name the list, and `SongProfile.Read` maps close variants onto it (mellow → chill, melancholic → melancholy, triumphant → epic) and drops anything else, so songs and requests share one vocabulary and the Music page can browse by mood. `Moods`, `Themes` and `GoodFor` are `text[]` columns; migration `StoreSongProfileListsAsArrays` converted them from JSON text and cleared `ProfiledAt` for songs holding a mood outside the list, so they're described again.

`MusicEmbeddingService.PrepareTracksAsync` describes new songs first, then embeds every song whose text changed: `SongProfile.Describe` → `Song: … Artist: … Album: … (year). Genre: …. Tags: …. Mood: …. Energy: …. Themes: …. Good for: …. Instrumental`. `MediaItemEmbeddings` (shared with films and their HNSW cosine index; films' queries filter to `IsBrowsableTitle`) records the SHA-256 `SourceHash` of that text and the embedding `Model`; a song is re-embedded when either differs, so new tags, a new profile or a new embedding model re-embed exactly the songs affected (`EmbeddingWrites.UpsertAsync`, which skips songs deleted mid-batch). The vector itself is derived data and stays out of `MediaItems`; the profile is song metadata and stays on the song.

Runs only while AI playlists are on: a music scan queues **Prepare Music for AI Playlists**; the nightly **Make AI Playlists** runs it first; both share the `music-ai` key so they never overlap. A batch that returns nothing stops the run instead of being paid for again. Artist tags come from the Last.fm popularity refresh (see `docs/music-and-audio.md`).

## Keeping a playlist

`POST /api/music/recommendations/mixes/{mixId}/save` turns any generated mix — Daily Mixes today, AI playlists next — into the profile's own unshared playlist, in order and filtered by its parental controls.

## The playlists (`AiPlaylistService`)

A profile's **taste** is the play-weighted mean of the vectors of the songs it played in the last 180 days (`GetPlayedVectorsAsync`), or none below 5 songs. Every song comes from `IAiPlaylistRepository.FindNearestTracksAsync`, which runs through `ApplyMusicAccess` with the **viewer's** filter. A song counts as a match only within `ServerSetting.AiPlaylistMatchWindow` (default 0.04, admin-editable 0.01–0.20 under For You → AI playlists) of the request's **10th**-nearest song, and never past 0.70 (`MatchLimit`). The limit is relative because how close good matches land shifts with how the request was worded; the 10th, not the 1st, because a song whose title repeats the request's words can land far ahead of the rest. Distance can't tell that a library has none of a genre — its nearest songs are then just its most similar ones. Fewer than 5 matches is *nothing found*; otherwise the list ends short rather than padding. Picks start at 2 per artist and let each artist in once more per pass until the list is full or the matches run out.

- **Weekly** (`AiPlaylist` ×4 + `Bridge`): profiles opted in, with ≥ 20 plays in 180 days and no weekly set in 7 days. One chat call (top artists + genres → 4 themes with title / why and a profile — genres, moods, energy, themes, good for, instrumental — and a bridge whose two ends are profiles), one embedding call for all of them (`SongProfile.ToText`, the same shape a song is described in). Theme query = 0.65 theme + 0.35 taste. The Bridge walks 20 equal steps from one end to the other, nearest unused song at each. Replaces last week's set.
- **Request** (`Requested`): one chat call reads the words into title, why, a profile, what to avoid, year range, length and whether the order matters; one embedding call. **Years apply only when the request names a year, decade or era** (`NamesAnEra`), whatever the model returns. When the request describes a progression ("builds from calm to loud", "winds down"), a second call orders the picked songs by each one's energy and mood (`OrderPrompt` / `ApplyOrder`, which keeps every song exactly once); other requests make no second call. An older plain `search` answer is still accepted. The listener can pick a length (10–60) in the dialog, which is used as-is; left on **Let AI decide**, the model chooses 10–60 to suit the request (25 when nothing suggests one). Query = 0.8 request + 0.2 taste, minus 0.35 × avoid. Limited to `AiPlaylistRequestsPerDay` per rolling day, counted from `AiUsageLogs` (the request's chat call is logged with the profile's `ProfileId`; weekly and embedding calls carry none), so deleting or regenerating a playlist can't reset the count. The newest 20 are kept. **Regenerate** rebuilds a request in place (same id and slot) from its saved `Prompt` or new words, at a chosen length, and counts as a request. The length isn't stored — nothing about the AI's reading is — so a regenerate is a fresh reading. **Requests and Blends can be deleted**; weekly ones are replaced each week. The request text is quoted as data in the prompt, never as instructions, and only the parsed fields are used.
- **Blend** (`Blend`): the midpoint of two tastes, 30 songs, **no chat call**. Partners are profiles with AI playlists on; a Blend whose partner later opts out is hidden. One Blend per partner, remade on request.

Scheduling: the nightly job queues "Make AI Playlists" (describe and embed new songs, then the due weekly sets); switching the feature on queues it at once; admins can force it (`POST /api/music/ai/generate`). If preparing new songs fails, the weekly sets are still made from the songs already embedded; a monthly-limit or OpenAI failure while making them stops the run for everyone.

## API

| Route | |
| --- | --- |
| `GET /api/music/ai` | `AiPlaylistsVM { enabled, requestsEnabled, weekly[], blends[], requests[] }` — `enabled` false when off or opted out |
| `POST /api/music/ai/requests` `{ prompt, songs? }` | `{ mixId }`; 400 empty or `songs` outside 10–60, 403 off, 404 nothing fits, 429 daily limit, 502 AI failed (ProblemDetails `detail` is shown as-is) |
| `POST /api/music/ai/requests/{mixId}/regenerate` `{ prompt?, songs? }` | `{ mixId }` (same id); same errors as a request, 404 if not this profile's request |
| `DELETE /api/music/ai/playlists/{mixId}` | 204; 404 unless it is this profile's request or Blend |
| `GET /api/music/ai/blend-partners` | `[{ profileId, name, imageUrl }]` |
| `POST /api/music/ai/blends` `{ partnerProfileId }` | `{ mixId }`; 404 partner unavailable, 409 not enough listening |
| `POST /api/music/ai/generate` | admin; 202 |

Made playlists are `GeneratedMix` rows (kinds 4–7, with `Description`, `Prompt`, `PartnerProfileId`), opened with the existing mix detail route (which now returns `kind`, `description`, `prompt`) and kept with `…/mixes/{id}/save`. They are **not** in `GET /recommendations/mixes`, which older clients read. The unique `(ProfileId, Kind, Slot)` index applies to them too, so a request takes the next free slot and a Blend keeps its partner's slot (or takes the next free one); the weekly set is numbered 1…n.

On the web, **Make me a playlist** and **Blend** sit in the Music page header on every sub-tab, and are absent when `GET /api/music/ai` says `enabled: false` (server off or profile opted out). The For You rows — **Made for you by AI** (weekly, Bridge, Blends) and **Your requests** — render only when they have tiles; there is no placeholder. Mix tracks and playlist items carry `globalListeners` for the Last.fm figure.
