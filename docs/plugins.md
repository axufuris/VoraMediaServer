# Plugin system

Plugins extend Vora at runtime with new providers — metadata sources, artwork sources, IPTV providers, collection sync sources, chronology providers, etc.

## Layout

- **`/src/Vora.Plugins/`** — single project containing:
  - `Interfaces/` — provider contracts (`IVoraPlugin`, `IArtworkProvider`, `ICollectionSyncProvider`, `ILibrarySyncProvider`, …) and adapter interfaces (`IPluginSettingsProvider`, `IRequestServerLookup`).
  - `Dtos/` — DTOs that cross the plugin boundary (`PluginSettingDefinitionDto`, `CollectionSyncItemDto`, `LibrarySyncPinDto`, …).
  - `Providers/<Source>/` — built-in concrete providers (e.g. `Tmdb/`, `Trakt/`, `Plex/`). One folder per upstream source; a folder may contain multiple provider classes (e.g. `Radarr/RadarrRequestProvider.cs` + `Radarr/RadarrCalendarProvider.cs`).
- **`<install>/Plugins/*.dll`** — external (third-party) plugin assemblies dropped in at runtime. The loader scans this folder recursively at startup. External plugins reference `Vora.Plugins` for the contracts and `Vora.Domain` only if entity shapes are required.

## Provider categories

Vora plugins fall into these provider interfaces (defined in `Vora.Plugins.Abstractions`):

- **Metadata provider** — looks up media metadata by external ID or title (e.g. TMDB, IMDB, TVDB). The built-in TMDB provider (`Providers/Tmdb/TmdbMetadataProvider.cs`) also maps `external_ids.tvdb_id` onto results — free for TV shows. **Movies** don't get a TVDB id from TMDB; the opt-in `ServerSetting.ResolveMovieTvdbIds` (off by default) makes the nightly metadata pass run an extra TVDB search to backfill missing movie **and** show `TvdbId`s (`MetadataManager.ResolveTvdbIdForMovieAsync` / `ResolveTvdbIdForShowAsync`). Admins can also trigger a one-time backfill via `POST /metadata/resolve-tvdb-ids` (Core Settings → "Resolve now"). Items that already have a `TvdbId`, and movies that come back empty, aren't re-searched every night.
- **Artwork provider** — fetches posters/backdrops/banners
- **IPTV provider** — supplies channels, EPG, stream URLs at the plugin contract level. Distinct from the user-facing `IptvPlaylist` / `IptvEpgSource` aggregates documented in `docs/iptv-and-dvr.md`; an IPTV provider plugin would typically feed data into those aggregates (e.g. HDHomeRun integration).
- **Collection sync provider** — pulls list contents from external sources (Trakt lists, custom feeds)
- **Chronology provider** — supplies a custom watch order for a collection (e.g. timeline-based MCU order)

When adding a **new** provider interface:

1. Add the interface to `Vora.Plugins.Abstractions`.
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

**Settings-only plugins** are also a thing — a plugin that's just a settings carrier for a built-in feature, with no provider interface. They implement `IVoraPlugin` directly and expose a `Type` string that names a new admin home (e.g. `YouTube` → `/admin/youtube`). The actual feature lives in the regular app layers (`Vora.Application`, `Vora.Api`, EF entities in `Vora.Domain`); the plugin only exists to declare the `api_key` / enable-toggle / region fields and let env-seeding work. See `Vora.Plugins/Providers/YouTube/YouTubePlugin.cs` and `docs/youtube.md`.

**External (third-party) plugins** are separate assemblies dropped into `<install>/Plugins/`. An external plugin project:

- References `Vora.Plugins` (and `Vora.Domain` only if entity shapes are required).
- Implements one or more of the provider interfaces.
- Builds to a `*.dll` placed under the `Plugins` folder the loader scans at runtime.

Plugin settings (API keys, endpoints) come through `IPluginSettingsProvider`. The settings UI on the admin pages reads/writes via `pluginAdminService` on the frontend, which calls the corresponding backend endpoint. New settings fields are declared by the plugin and surfaced generically — you don't have to hand-build admin UI for them.

## Where plugin settings render in the admin UI

The `PluginSection` component in `components/Admin/Settings/PluginSettingsTab.tsx` renders one plugin's settings inline. It's exported so feature pages can mount it directly. The `FeaturePluginList` wrapper in `components/Admin/Features/FeaturePluginList.tsx` takes a list of plugin type names (matching the interface name without the `I` prefix and `Provider` suffix — e.g. `Discovery` for `IDiscoveryProvider`), fetches plugins, filters, and renders a `PluginSection` per match.

Each plugin category has a canonical home:

| Plugin type | Lives on |
| --- | --- |
| `Discovery`, `Theater` | Discover (`/admin/discovery`) |
| `Recommendation` | For You (`/admin/for-you`) |
| `Calendar` | Release Calendar (`/admin/release-calendar`) |
| `PodcastDiscovery` | Podcasts (`/admin/podcasts`) |
| `YouTube` | YouTube (`/admin/youtube`) |
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

Theme authors don't need to write or compile C# — a bundle is just JSON + images. If you ever expand this to support compiled themes (with React-component slot overrides), that becomes a code plugin and lives in `<install>/Plugins/` like everything else; the contracts would go in `Vora.Plugins.Abstractions` next to the existing provider interfaces.
