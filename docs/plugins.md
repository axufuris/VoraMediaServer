# Plugin system

Plugins extend Vora at runtime with new providers — metadata sources, artwork sources, IPTV providers, collection sync sources, chronology providers, etc.

## Layout

- **`/src/Vora.Plugins/`** — single project containing:
  - `Interfaces/` — provider contracts (`IVoraPlugin`, `IArtworkProvider`, `ICollectionSyncProvider`, `ILibrarySyncProvider`, …) and adapter interfaces (`IPluginSettingsProvider`, `IRequestServerLookup`).
  - `Dtos/` — DTOs that cross the plugin boundary (`PluginSettingDefinitionDto`, `CollectionSyncItemDto`, `LibrarySyncPinDto`, …).
  - `Providers/<Source>/` — built-in concrete providers (e.g. `Tmdb/`, `Trakt/`, `Plex/`). One folder per upstream source; a folder may contain multiple provider classes (e.g. `Radarr/RadarrRequestProvider.cs` + `Radarr/RadarrCalendarProvider.cs`).
- **`<install>/Plugins/*.dll`** — external (third-party) plugin assemblies dropped in at runtime. The loader scans this folder recursively at startup. External plugins reference `Vora.Plugins` for the contracts and `Vora.Domain` only if entity shapes are required.

## Provider categories

Vora plugins fall into these provider interfaces (defined in `Vora.Plugins/Interfaces/`):

- **Metadata provider** — looks up media metadata by external ID or title (e.g. TMDB, IMDB, TVDB). The built-in TMDB provider (`Providers/Tmdb/TmdbMetadataProvider.cs`) also maps `external_ids.tvdb_id` onto results — free for TV shows. **Movies** don't get a TVDB id from TMDB; the opt-in `ServerSetting.ResolveMovieTvdbIds` (off by default) makes the nightly metadata pass run an extra TVDB search to backfill missing movie **and** show `TvdbId`s (`MetadataManager.ResolveTvdbIdForMovieAsync` / `ResolveTvdbIdForShowAsync`). Admins can also trigger a one-time backfill via `POST /metadata/resolve-tvdb-ids` (Core Settings → "Resolve now"). Items that already have a `TvdbId`, and movies that come back empty, aren't re-searched every night.
- **Artwork provider** — fetches posters/backdrops/banners
- **IPTV provider** — supplies channels, EPG, stream URLs at the plugin contract level. Distinct from the user-facing `IptvPlaylist` / `IptvEpgSource` aggregates documented in `docs/iptv-and-dvr.md`; an IPTV provider plugin would typically feed data into those aggregates (e.g. HDHomeRun integration).
- **Collection sync provider** — pulls list contents from external sources (Trakt lists, custom feeds)
- **Chronology provider** — supplies a custom watch order for a collection (e.g. timeline-based MCU order)

When adding a **new** provider interface:

1. Add the interface to `Vora.Plugins/Interfaces/`.
2. Add it to the `PluginProviderInterfaces` array in `Vora.Api/Extensions/PluginLoaderExtensions.cs` so the loader discovers it.

## Loader

`Vora.Api/Extensions/PluginLoaderExtensions.cs` exposes `services.AddVoraPlugins(path)`. It:

- Scans built-in and external assemblies under the given path.
- Finds every concrete `IVoraPlugin` and every concrete implementation of one of the `PluginProviderInterfaces`.
- Registers them with DI.

**Constraint — plugins are not hot-unloadable.** External plugin DLLs are loaded into `AssemblyLoadContext.Default` (`PluginLoaderExtensions.cs`), which is non-collectible: once loaded, an assembly stays loaded for the lifetime of the process and the DLL file stays locked on disk. This is why uninstall does **not** delete the DLL directly — `PluginManager` renames it to `*.dll.deleted` (`File.Move(..., assemblyPath + ".deleted")`) and the loader skips `.deleted` files on the next startup; the actual removal happens on restart. Enabling/disabling a plugin is a DB flag, not a load/unload. Plugin code also runs at full host trust (no sandbox). If true hot-unload is ever needed, each plugin would have to load into its own collectible `AssemblyLoadContext` — a larger change than the current model warrants.

`Vora.Api/Extensions/PluginSettingsAdapter.cs` adapts the application-layer `ISystemSettingsRepository` to the plugin-facing `IPluginSettingsProvider`, so plugins can read/write their own settings without knowing about EF Core.

`Vora.Application/Requests/RequestServerLookupAdapter.cs` implements `IRequestServerLookup` (declared in `Vora.Plugins.Interfaces`). Plugins that need to consume credentials owned by a different aggregate — most prominently the Radarr/Sonarr calendar providers — resolve `IRequestServerLookup` and ask for "calendar servers" by request-provider id (e.g. `radarr_requester`). This is how the calendar plugins share credentials with the Request Servers admin page; see the section on the `ProvidesReleaseCalendar` flag in `docs/database.md`.

## Seeding plugin settings from environment variables

`Vora.Application/Plugins/PluginSettingsEnvSeeder.cs` runs as a startup task (after database migrations, before workers). It reads `Vora:PluginSettings:<pluginId>:<settingKey>` from `IConfiguration` — env var form `Vora__PluginSettings__<pluginId>__<settingKey>` — and writes values into the database **only when the row is empty**. Once a value lives in the database, the seeder leaves it alone so admin-UI edits survive container restarts.

The seeder validates that `<pluginId>` matches a registered `IVoraPlugin.Id` and that `<settingKey>` exists in that plugin's `GetSettingDefinitions()` (or is the special `is_enabled` toggle). Anything else is skipped with a `WARN` log. Values are redacted from logs — only key names appear, so seeded API keys never end up in the in-app log viewer or the file sink.

User-facing docs and the full plugin/setting matrix live in the README's **Bootstrapping plugin API keys from environment variables** section.

## Writing a plugin

**Built-in providers** live inside the `Vora.Plugins` project. To add one: drop a new file under `Providers/<Source>/<Whatever>Provider.cs`, implement `IVoraPlugin` plus the relevant provider interface, and the loader picks it up automatically on the next build.

**External (third-party) plugins** are separate assemblies dropped into `<install>/Plugins/`. An external plugin project:

- References `Vora.Plugins` (and `Vora.Domain` only if entity shapes are required).
- Implements one or more of the provider interfaces.
- Builds to a `*.dll` placed under the `Plugins` folder the loader scans at runtime.

Plugin settings (API keys, endpoints) come through `IPluginSettingsProvider`. The settings UI on the admin pages reads/writes via `pluginAdminService` on the frontend, which calls the corresponding backend endpoint. New settings fields are declared by the plugin and surfaced generically — you don't have to hand-build admin UI for them.

### Server-wide metadata language

`IPluginSettingsProvider.GetMetadataLanguageAsync()` returns the admin's chosen metadata language (Admin → Settings → Core) as a **TVDB-style ISO 639-2 (3-letter) code** — e.g. `"eng"`, `"kor"`. Any metadata/artwork provider should read it so titles, overviews, and localized artwork come back in the admin's language instead of the item's original language. TVDB endpoints take the 3-letter code verbatim; for APIs that expect ISO 639-1 (2-letter, e.g. TMDB) call `Vora.Plugins.MetadataLanguageCodes.ToIso6391(code)` rather than re-deriving the table — adding a language to the admin dropdown then lights up every provider at once. Prefer skipping the extra translation call when the item is already in the target language (TVDB exposes `originalLanguage`), and always fall back to the original language when no translation exists. Built-in consumers: `TvdbMetadataProvider`, `TmdbMetadataProvider`, `TmdbArtworkProvider`.

### Setting definitions, required fields, and connection tests

A plugin declares its settings by returning `PluginSettingDefinitionDto`s from `GetSettingDefinitions()`. Each definition carries `Key`, `Label`, `Type` (`text`, `password`, `select`, …), `DefaultValue`, `Description`, `Options` (for `select`), plus two fields that shape the generic admin form:

- **`Required`** — renders a `*` on the label. It's a UI affordance for "you should fill this in"; it does not by itself block saving.
- **`Placeholder`** — greyed example text in the input (e.g. `https://radarr.example.com`).

Two plugin-level flags surface on `PluginVM` and drive admin UX:

- **`RequiresConfiguration`** (computed, not authored) — true when any definition has an empty `DefaultValue` **and** no saved value yet (`PluginManager.RequiresConfiguration`). The admin list uses it to badge plugins that are installed but not yet usable.
- **`ExternalConfigurationHint`** (`IVoraPlugin.ExternalConfigurationHint`, default `null`) — a short string for plugins whose credentials live elsewhere, so the settings form shows a pointer instead of empty fields. The Radarr/Sonarr **calendar** providers set this because their credentials come from Request Servers via `IRequestServerLookup` (see the `ProvidesReleaseCalendar` note in `docs/database.md`); they return no setting definitions of their own.

**Connection test.** A plugin that can verify its credentials implements `IPluginConnectionTest.TestConnectionAsync(settings, ct)` and returns `PluginConnectionTestResult.Ok(msg)` / `.Fail(msg)` (exceptions are caught and reported as a failure, 15s timeout). `PluginManager` sets `SupportsConnectionTest = plugin is IPluginConnectionTest`; when true, the admin form renders a **Test connection** button that POSTs the current (unsaved) field values to `POST /api/settings/plugins/{pluginId}/test` (AdminOnly) and shows the ✓/✕ message inline. Test against a cheap, auth-only upstream endpoint — the built-in providers (TMDB, TVDB, OMDb, Fanart, MDbList, Last.fm, Genius, SerpApi) each probe a lightweight authenticated call.

## Where plugin settings render in the admin UI

The `PluginSection` component in `components/Admin/Settings/PluginSettingsTab.tsx` renders one plugin's settings inline. It's exported so feature pages can mount it directly. The `FeaturePluginList` wrapper in `components/Admin/Features/FeaturePluginList.tsx` takes a list of plugin type names (matching the interface name without the `I` prefix and `Provider` suffix — e.g. `Discovery` for `IDiscoveryProvider`), fetches plugins, filters, and renders a `PluginSection` per match.

Each plugin category has a canonical home:

| Plugin type | Lives on |
| --- | --- |
| `Discovery`, `Theater` | Discover (`/admin/discovery`) |
| `Recommendation` | For You (`/admin/for-you`) |
| `Calendar` | Release Calendar (`/admin/release-calendar`) |
| `PodcastDiscovery` | Podcasts (`/admin/podcasts`) |
| `Artwork`, `Metadata`, `Ratings`, `FolderWatcher`, `LocalScanner`, `Chronology` | Libraries (TBD) |
| `CollectionSync` | Collections (TBD) |
| `OverlayEngine` | Poster Overlays (TBD) |
| `Request` | Request Queue (TBD) |
| `Lyrics`, `ListeningData` | Music (TBD) |

Categories marked TBD don't have a dedicated admin page yet, so their plugin settings are still reachable via the System Settings → Plugins sub-sidebar. That sub-sidebar filters out the homed categories automatically — when every category has a home, the section disappears entirely.

The **Plugin Management** page at `/admin/plugins` is a separate concern: it lists every installed plugin and lets admins upload, enable, disable, and uninstall whole plugin packages. It does not host per-plugin settings.

## Admin theme bundles (separate from code plugins)

Admin themes are NOT code plugins. They're folder bundles at `<install>/Themes/<theme-id>/` containing a `manifest.json` and an optional `assets/` directory. They have their own loader (`IThemeBundleLoader` in `Vora.Application.Themes`) and are deliberately kept outside `<install>/Plugins/` so the code-plugin loader's recursive `*.dll` scan never walks into theme assets.

Author guide: `docs/admin-theme-bundles.md`. Surface in the admin UI: **Admin → Server → Appearance** (`/admin/appearance`).

Theme authors don't need to write or compile C# — a bundle is just JSON + images. If you ever expand this to support compiled themes (with React-component slot overrides), that becomes a code plugin and lives in `<install>/Plugins/` like everything else; the contracts would go in `Vora.Plugins/Interfaces/` next to the existing provider interfaces.
