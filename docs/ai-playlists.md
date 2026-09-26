# AI playlists (music)

Playlists made from a profile's listening with OpenAI: a weekly **Made for you by AI** row, **Bridges** between two of a profile's sounds, **Blends** between two profiles, and a **Make me a playlist for…** box. Built so cost stays small on very large libraries.

## The rule that keeps it cheap and honest

**The chat model never picks songs.** Songs come from vector search over the library's own embeddings, filtered by the viewer's parental controls, so nothing invented or unavailable can appear. The chat model only names themes and reads requests — small prompts, once a week per profile or once per request.

| Step | Cost |
| --- | --- |
| Embed each song once (`text-embedding-3-small`, ~40 tokens) | ~$0.01 per 5,000 songs, once |
| A profile's taste (mean of the vectors of what it plays) | database only |
| Weekly themes and names | one small chat call per profile per week |
| A request | one embedding plus one small chat call |

Every call goes through `IOpenAiClient` (`CompleteJsonAsync` / `EmbedAsync`): the OpenAI plugin's key, its monthly token limit, and a row in AI Usage & Stats under plugin id `openai_music_playlists`.

## Switches

- **Server** (`ServerSetting`, admin **Features → For You**): `EnableAiMusicPlaylists` (default **off**), `EnableAiPlaylistRequests` (default on, meaningful only with the first), `AiPlaylistRequestsPerDay` (default 10). Turning the first on queues the library's embeddings at once.
- **Derived flags** (`FeatureFlagsVM`): `AiPlaylists` = toggle && For You && an OpenAI key; `AiPlaylistRequests` = that && the requests toggle. Clients gate on these.
- **Profile** (`UserProfile.AiMusicPlaylistsEnabled`, default on; `PUT /api/users/profiles/{id}/ai-playlists`, account owner only): off means nothing about the profile's listening is sent to OpenAI, it gets no AI playlists or requests, and no one can Blend with it.

## Song embeddings

`MusicEmbeddingService` embeds songs with no row in `MediaItemEmbeddings` (the same table and HNSW cosine index films use; films' queries filter to `IsBrowsableTitle`, so songs never leak into film recommendations). Text: `Song: … Artist: … Album: … (year). Genre: …`. Runs only while AI playlists are on: after every music scan ("Preparing music for AI playlists…"), with the nightly AI job, and when the toggle is first switched on. A batch that returns nothing stops the run instead of being paid for again.

## Keeping a playlist

`POST /api/music/recommendations/mixes/{mixId}/save` turns any generated mix — Daily Mixes today, AI playlists next — into the profile's own unshared playlist, in order and filtered by its parental controls.

## The playlists (`AiPlaylistService`)

A profile's **taste** is the play-weighted mean of the vectors of the songs it played in the last 180 days (`GetPlayedVectorsAsync`), or none below 5 songs. Every song comes from `IAiPlaylistRepository.FindNearestTracksAsync`, which runs through `ApplyMusicAccess` with the **viewer's** filter, at most 2 songs per artist.

- **Weekly** (`AiPlaylist` ×4 + `Bridge`): profiles opted in, with ≥ 20 plays in 180 days and no weekly set in 7 days. One chat call (top artists + genres → 4 themes with title / why / search phrase, and a bridge's two ends), one embedding call for all phrases. Theme query = 0.65 theme + 0.35 taste. The Bridge walks 20 equal steps from one end to the other, nearest unused song at each. Replaces last week's set.
- **Request** (`Requested`): one chat call reads the words into title, why, search phrase, what to avoid, year range and length (10–60); one embedding call. Query = 0.8 request + 0.2 taste, minus 0.35 × avoid. Limited to `AiPlaylistRequestsPerDay` per rolling day; the newest 20 are kept. The request text is quoted as data in the prompt, never as instructions, and only the parsed fields are used.
- **Blend** (`Blend`): the midpoint of two tastes, 30 songs, **no chat call**. Partners are profiles with AI playlists on; a Blend whose partner later opts out is hidden. One Blend per partner, remade on request.

Scheduling: the nightly job queues "Make AI Playlists" (embed new songs, then the due weekly sets); switching the feature on queues it at once; admins can force it (`POST /api/music/ai/generate`). A monthly-limit or OpenAI failure stops the run for everyone.

## API

| Route | |
| --- | --- |
| `GET /api/music/ai` | `AiPlaylistsVM { enabled, requestsEnabled, weekly[], blends[], requests[] }` — `enabled` false when off or opted out |
| `POST /api/music/ai/requests` `{ prompt }` | `{ mixId }`; 400 empty, 403 off, 404 nothing fits, 429 daily limit, 502 AI failed (ProblemDetails `detail` is shown as-is) |
| `GET /api/music/ai/blend-partners` | `[{ profileId, name, imageUrl }]` |
| `POST /api/music/ai/blends` `{ partnerProfileId }` | `{ mixId }`; 404 partner unavailable, 409 not enough listening |
| `POST /api/music/ai/generate` | admin; 202 |

Made playlists are `GeneratedMix` rows (kinds 4–7, with `Description`, `Prompt`, `PartnerProfileId`), opened with the existing mix detail route (which now returns `kind`, `description`, `prompt`) and kept with `…/mixes/{id}/save`. They are **not** in `GET /recommendations/mixes`, which older clients read.
