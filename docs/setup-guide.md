# Setup guide

A first-run walkthrough for server admins at `/admin/setup` (and `/admin/server/:serverId/setup`). It asks a short set of questions, shows what the server has now for each, and saves as it goes. It is a page inside the admin shell, not a modal, because the plugin steps hold real forms.

## State

Three columns on `ServerSetting` (migration `AddSetupGuide`):

| Field | Meaning |
| --- | --- |
| `SetupGuideStatus` | `NotStarted`, `InProgress`, `Skipped`, `Completed` (`Vora.Domain/Enums/SetupGuideStatus.cs`) |
| `SetupGuideStep` | The step the admin was last on (≤ 64 chars), so the guide reopens there |
| `SetupGuideContent` | `[Flags]` answer to "What will you put on this server?": `MoviesAndShows`, `Music`, `LiveTv`, `InternetRadio`, `Podcasts`. Defaults to movies/shows + music |

`GET` / `PUT /api/settings/setup-guide` (admin only) read and write all three as `SetupGuideVM` (`status`, `step`, and one boolean per content kind). The PUT validates the status, trims the step and cuts it to 64 characters. It lives on its own endpoint because the big `PUT /api/settings/server` overwrites every field it carries.

The migration marks the guide `Skipped` on servers that already have an admin user (`WHERE EXISTS (SELECT 1 FROM "Users" WHERE "IsAdmin")`), so running servers such as QA are not nagged. A brand-new database has no admin yet and keeps `NotStarted`. `PostgresSetupGuideMigrationTests` covers both.

## When it opens

- **First sign-in.** `ProfileSelectionPage` sends a freshly claimed server's admin to `/admin/setup` (it used to send them to System Settings). Opening the guide moves `NotStarted` to `InProgress`.
- **Pop-up.** `SetupGuidePrompt`, mounted in `AdminShell`, asks once per browser session per server when the status is `NotStarted` or `InProgress`: **Start setup / Continue setup**, **Not now** (until the next session), **Skip setup** (for good). Opening the guide counts as being asked, so leaving the guide midway does not bring the pop-up straight back. The key is the sessionStorage `setup_guide_prompted_<serverId|local>` (`components/Admin/SetupGuide/setupGuideSession.ts`).
- **Dashboard.** `SetupGuideBanner` shows "Set up your server" / "Finish setting up Vora" while the guide is unfinished, and nothing after.
- **System Settings.** A **Run setup guide** button in the page header, always. There is deliberately no sidebar entry.

Re-running a finished or skipped guide keeps its status: every step is open from the rail, and the header button reads **Close guide** instead of **Skip setup**.

## Steps

`pages/Admin/SetupGuide/setupSteps.ts` builds the list from the content answers and whether Discover has rows:

| Group | Steps | Saves to |
| --- | --- | --- |
| Basics | Welcome, Your server, Playback, What you'll add | `PUT /settings/server` (name, `metadataLanguage`, `scheduleTimeZone`, `streamingProfile`, hardware acceleration + encoding, device); content answers go to the guide and switch Live TV / radio / podcasts on or off in `PUT /settings/features` |
| Movies & TV | Metadata, Artwork, Ratings, Skip intro & credits, Preview thumbnails, Requests, Subtitles, Discover | plugin settings; `runDetections`, `detectionScheduleTime`, `analyzeUseHardwareDecode`, `videoThumbnail*`; request servers; `PUT /discovery/config` + the Discover flag |
| Live TV & radio | Live TV, Internet radio | feature flags, `POST /iptv/admin/playlists` (kind Tv / Radio), `POST /iptv/admin/epg-sources`, the DVR flag |
| Podcasts | Podcasts | feature flag, `GET /podcasts/search`, `POST /podcasts/admin/catalog` |
| Music | Last.fm, Lyrics | plugin settings |
| AI | AI features, then Done | OpenAI plugin settings, `enableAiMusicPlaylists` |

- **Discover** appears only when `GET /discovery/config` returns rows, i.e. once a Discover provider has a key: TMDB (`tmdb_discovery`, using the TMDB key) or MyAnimeList (`mal_discovery`, using the `mal_artwork` client id, offered on the Artwork step). Rows are re-read after any plugin save.
- Intro detection and thumbnails sit under Movies & TV because they only apply to video. Both steps say the first run on a large library takes a while.
- The OMDb card and the plugin's own description both explain that the free key's 1,000 daily lookups mean a large library takes a few days to get every score; ratings are picked up again on later library scans.

## Saving

Each step saves when the admin presses **Save and continue**, **Back** or a rail step, never only at the end:

- The page tracks dirty server settings, feature flags and Discover rows and sends them whole (`ServerSettings` and `FeatureFlagsVM` are always loaded first, because those PUTs replace every field).
- Cards register a saver through `SetupSaverContext` / `useStepSaver` (`setupSaver.ts`). On navigation the page runs every registered saver first, so a key typed but not saved, or a Radarr connection tested but not saved, is not lost.
- **Skip this step** moves on without saving. Basics steps cannot be skipped.
- The step id is written to the guide on every move.

## Building blocks

- `SetupPluginCard`: loads the plugin's own field definitions and help text (`GET /settings/plugins/{id}`), shows only the listed `fieldKeys` (module constants in `setupContext.ts`, `PLUGIN_FIELDS`; never inline arrays, they are an effect dependency), saves with `is_enabled: 'true'`, and runs the plugin's connection test when it has one. Plugin saves are per-key upserts, so showing a subset is safe. Other options stay on the Plugins page, linked from each card.
- `SetupRequestServerCard`: Radarr / Sonarr with address, port, API key, URL base, HTTPS, **Use for the Release Calendar** (on by default), then after **Test connection** the quality profile, root folder, minimum availability (Radarr only) and **Search automatically when a request comes in**. The explanations live in `components/Admin/Settings/requestServerText.ts`, shared with System Settings → Request Servers. An existing server of that kind is shown read-only with a link to System Settings.
- `SetupLiveCards`: playlist (presets from `utils/iptvPresets.ts`, shared with the Live TV / Radio pages), TV guide and podcast catalog cards.

## Fixed alongside

- **Save defaults.** `ServerSettingsVM`'s property defaults disagreed with `ServerSetting`'s for 18 fields (hardware acceleration and encoding, streaming profile, tone mapping, HEVC, GPU transcode limit, thumbnail size, …), so a settings PUT that left a field out reset it. They now match, and `SetupGuideSettingsTests` fails if a default VM ever differs from the projection of a default entity.
- **OpenAI connection test.** `OpenAiRecommendationProvider` implements `IPluginConnectionTest` (`GET https://api.openai.com/v1/models`): 401/403 rejected, 429 accepted but no billing or credit.
- **New libraries follow the setup.** `CreateLibrary` replaces any default provider that has no key with one that does (`utils/libraryProviderDefaults.ts`: TV metadata falls back from TVDB to TMDB, IMDb ratings from OMDb to TMDB, a second rating never repeats the first), and pre-ticks preview thumbnails and intro/credit detection when those are on server-wide.
- The System Settings "In-Memory Cache" card was removed: its `cacheSizeLimitMb` had no backend field and was dropped on every save.
- Time zones, metadata languages and IPTV presets are shared modules (`utils/serverSettingOptions.ts`, `utils/iptvPresets.ts`) instead of copies.
