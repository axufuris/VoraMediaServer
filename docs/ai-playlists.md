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
