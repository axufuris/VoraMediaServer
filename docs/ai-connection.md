# The AI connection

Every AI feature sends its requests to one place: OpenAI by default, or any server that speaks OpenAI's API (Ollama, LM Studio, llama.cpp, vLLM, LocalAI, LiteLLM, OpenRouter). This file covers where that is configured, how requests are sent, and the rules that keep stored vectors comparable.

Feature-level detail lives elsewhere: `docs/ai-playlists.md` (music), `docs/collections.md` (lists and story order).

## Settings

All on plugin `openai_recommendations` (`AiConnection.PluginId`). The keys are constants on `AiConnection`.

| Key | Default | What it is |
|---|---|---|
| `base_url` | `https://api.openai.com/v1` | Where the API starts. |
| `api_key` | empty | Required for OpenAI. Optional anywhere else. |
| `chat_model` | `gpt-4o-mini` | Model for the "For You" rows, and the fallback for the next two. |
| `collections_model` | `gpt-5` (shown in the form) | Model for AI lists and story order. |
| `music_playlists_model` | empty | Model for song profiles and playlist requests. |
| `embedding_model` | `text-embedding-3-small` | Model that makes the vectors. |
| `schedule_time`, `monthly_token_limit`, `guardrails` | `02:00`, `0`, empty | Nightly run, token ceiling, house rules. |

**A server that never saved these behaves exactly as before.** `AiConnection` reads an unset `base_url` as OpenAI's, an unset `chat_model` as `gpt-4o-mini`, and an unset `embedding_model` as `text-embedding-3-small`. The form shows those same values, so opening and saving it changes nothing.

**Model fallback.** A feature model that is empty uses `chat_model`. That includes `collections_model`: the form shows `gpt-5` as its default, but a server that never saved the form has no value stored, so its collections run on `chat_model`. This was already true before the address became configurable and is left as it was.

**On a server other than OpenAI nothing is assumed.** There is no default model: `chat_model` must be chosen before AI counts as set up (`AiConnection.CanChat`), and an empty `embedding_model` means no embeddings (`CanEmbed` false). Collections then still work; recommendations and AI playlists are off.

`AiConnection.ParseBaseUrl` tidies what was typed: adds `http://` when there is no scheme, adds `/v1` when there is no path, and strips a pasted `/chat/completions`, `/embeddings` or `/models`. Anything that is not http or https is refused.

Env seeding works for the new keys like any other plugin setting (`Vora__PluginSettings__openai_recommendations__base_url`, …).

## The layers

| Class | Job |
|---|---|
| `AiConnection` | Saved settings read into facts: address, key, model per feature, limits. Pure; no I/O beyond `LoadAsync`. |
| `IAiEndpoint` / `AiEndpoint` | The wire: chat, embeddings and the model list for a given connection. No settings, no usage log. |
| `IOpenAiClient` / `OpenAiClient` | What features call. Loads the saved connection, enforces the monthly token limit, adds the guardrails, logs usage. |
| `IAiConnectionTester` | Tests and lists models for the values in a form that has not been saved, and reports status. |

**Every AI call goes through `IOpenAiClient`.** The "For You" rows (`OpenAiRecommendationProvider`) and the nightly movie and show embeddings (`MediaEmbeddingService`) used to call OpenAI on their own, which skipped the monthly token limit and the guardrails. Do not add another direct call: a new feature takes `IOpenAiClient`.

## What `AiEndpoint` tolerates

Local servers differ from OpenAI in small ways. Each is handled once, here.

- **JSON mode refused.** A chat request is sent with `response_format: json_object`, plus `temperature` or, for reasoning models (`AiModelNames.IsReasoning`: `gpt-5*`, `o1*`, `o3*`, `o4*`, with or without a vendor prefix), `reasoning_effort: low`. A 400 or 422 is retried once with none of them. If that works, the server and model are remembered for 12 hours and sent plain requests from then on.
- **Chatty answers.** `AiJson.Extract` drops `<think>…</think>` blocks, code fences and any text around the outermost `{…}`. OpenAI's JSON-mode answers pass through unchanged.
- **No key.** No `Authorization` header is sent when the key is empty.
- **Embeddings without an index.** Vectors are paired to inputs by the `index` the server returns; when a server sends none, by position.
- **Slow servers.** Chat allows 15 minutes and embeddings 10 on a server other than OpenAI (5 and 2 on OpenAI). Embedding inputs go in batches of 32. Song profiling runs one request at a time (`IOpenAiClient.GetParallelismAsync`; six on OpenAI), because one home GPU answers one request at a time and the rest timed out in its queue.
- **A timeout is not a cancel.** `HttpClient.Timeout` surfaces as `TaskCanceledException`, which callers treat as the task being cancelled. `AiEndpoint` turns it into an `AiEndpointException` that says the server was slow.

Failures are `AiEndpointException` (an `InvalidOperationException`, with the HTTP status when there was one). Messages for OpenAI are the ones it always gave; other servers get the host, and for localhost the Docker explanation.

## Vectors: one length, one model at a time

`MediaItemEmbeddings.Embedding` is `vector(1536)` with an HNSW cosine index. Three rules follow.

1. **Shorter vectors are padded with zeros** to 1536 (`AiEndpoint.Fit`). Cosine similarity between two padded vectors equals that of the originals, so the column and index work for any model up to 1536. `AiEndpointTests` pins this.
2. **Longer vectors are asked for again with `dimensions: 1536`** (which `text-embedding-3-large` supports). If the server refuses or still sends more, the call fails with a message naming the model. The connection test catches this before anything is saved.
3. **Every row carries the model that made it** (`MediaItemEmbedding.Model`, the name cut to 64 characters by `AiConnection.Stamp`). Vectors from two models cannot be compared, so:
   - searches only read rows of the current model: `VectorSearchUnwatchedMediaAsync`, `FindNearestTracksAsync` (`AiTrackFilter.EmbeddingModel`) and `GetPlayedVectorsAsync`;
   - "waiting for an embedding" means "not yet read by the current model", for movies and shows (`GetMediaItemsMissingEmbeddingsAsync`) as it already did for songs;
   - saving replaces the old row (`EmbeddingWrites.UpsertAsync`).

**Rows with no model are OpenAI's.** Rows written before the model was recorded, which in practice means movies and shows, have `Model = null`. They count as `text-embedding-3-small` (`AiConnection.IsLegacyStamp`), so a server that stays on OpenAI is not read again. No migration was needed: the column already existed for songs.

**Changing the embedding model empties recommendations and AI playlists until the library is read again**, on the nightly run or at once. `POST /api/admin/ai-stats/trigger` now queues both the movie and show embeddings and the song preparation; the AI Settings form offers it after such a save.

**The match cut-offs were tuned on `text-embedding-3-small`** (`OpenAiRecommendationRepository.CosineDistanceThreshold`, the `AiPlaylistService` match window). Other models spread their distances differently, so results will be looser or tighter. Nothing breaks; this has not been measured on another model.

## Testing a connection

`IAiConnectionTester.TestAsync`, reached through the plugin's `IPluginConnectionTest` (`POST /api/settings/plugins/openai_recommendations/test`), works on the form's values:

1. No usable address, or OpenAI without a key: fails without calling anyone.
2. `GET {base}/models`. For OpenAI the refusals keep their wording (401/403, 429, other).
3. A chosen model the server does not list fails by name.
4. The embedding model is tried with one word, unless it is OpenAI's default, so the plain OpenAI test costs nothing.

`POST /api/settings/ai/models` returns the server's models split into chat and embedding lists (`AiModelNames`: names containing `embed`, `bge-`, `e5-`, `gte-` or `minilm` are embeddings; on OpenAI, speech, image and moderation models are left out). The split only feeds the pickers; any name can still be typed.

`GET /api/settings/ai/status` says whether the saved connection can chat and embed. The setup guide's summary and the AI Settings badge read it, so "is AI set up" has one definition.

## Web

- **AI Settings** (`/admin/ai-settings`, always in the sidebar) is the home for all of this. AI Usage & Stats links to it.
- `components/Admin/Ai/`: `AiSettingsForm` (the whole form), `AiConnectionFields` (address, key, the four model pickers), `AiProviderGuide` + `aiProviders.ts` (presets, like the email provider guide), `ModelPicker` (the server's list, or a typed name), `useAiSettings` (state, save, test, model loading).
- The same form is shown in the plugin's panel on the Plugins page, and `SetupAiCard` reuses `AiConnectionFields` in the setup guide, so there is one set of fields.
- **Picking a different provider clears the API key and the model names.** A key belongs to the server it was made for; left in the field it would be sent to the next one.
- An empty address means OpenAI on a server that never saved one, but "Another server" also leaves it empty for the admin to fill in. `currentAiProvider` uses the choice made in the form to tell them apart.

## Cost on the usage page

AI Usage & Stats estimates cost in `utils/aiPricing.ts`: OpenAI's standard list prices per million tokens, looked up by the exact model name (a dated snapshot and an `openai/` prefix resolve to their model). A model with no price there, such as one from another company or one on the admin's own computer, adds nothing and is named under the figure. Add a row when Vora starts offering a new OpenAI model; the old "name contains" rule left `gpt-5` at $0.

## Not done

- No request has been sent to a real Ollama or LM Studio server. The behaviour above is covered by tests against stand-in servers.
- The cut-offs are not calibrated per embedding model (see above).
- Embedding models that need a prefix on each input (`search_document:`, `query:`) are sent the plain text.
