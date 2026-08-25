# Vora

A self-hosted media server with a cinematic, template-themed client. Vora
indexes your media library and surfaces it through a modern UI with
support for movies, TV, music, IPTV with DVR, multi-server clients,
profiles, plugins, and scheduled visual templates.

## Tech stack

- **Backend:** .NET 10, C#, minimal APIs (`Vora.Api`)
- **Database:** PostgreSQL with the `vector` extension, EF Core
- **Real-time:** SignalR (`/hubs/Vora`)
- **Frontend:** React + TypeScript + Vite + Tailwind (`Vora.Web`)
- **Container:** Single Docker image bundling the API and React client;
  FFmpeg + Comskip included for transcoding and DVR commercial detection

## Running Vora

Vora is distributed as a Docker image at
`ghcr.io/<owner>/vora-media-server`. The container serves both the API
and the bundled React client on port `8080`. It expects a PostgreSQL
database with the `vector` extension enabled.

The full deployment is two pieces:

1. A PostgreSQL server (with `pgvector`).
2. The Vora container itself.

When the Vora container starts in any non-`Development` environment, it
automatically applies any pending EF Core migrations against the
configured database — so there's no manual schema step. You only need
to make sure the database exists and is reachable.

The sections below walk through each piece on a single Linux host using
Docker, with an Unraid-specific walkthrough at the end. The same
pattern works on TrueNAS Scale or any host with Docker installed; tweak
volume paths to match your system.

### 1. Set up PostgreSQL with `pgvector`

The recommended path is the official `pgvector/pgvector` image, which
ships PostgreSQL with the `vector` extension prebuilt. Create a
dedicated data directory on the host and start the container:

```bash
mkdir -p /srv/vora/postgres-data

docker run -d \
  --name vora-postgres \
  --restart unless-stopped \
  -e POSTGRES_DB=vora \
  -e POSTGRES_USER=vora \
  -e POSTGRES_PASSWORD=change-me-to-a-strong-password \
  -p 5432:5432 \
  -v /srv/vora/postgres-data:/var/lib/postgresql/data \
  pgvector/pgvector:pg16
```

A few notes:

- Pin a specific Postgres major (`pg16`, `pg17`, …) rather than a moving
  tag. Postgres major upgrades require a manual `pg_upgrade` step.
- If Vora and Postgres will share a Docker network (recommended — see
  the compose example below), drop the `-p 5432:5432` publish so the
  database isn't exposed on the host's public interface.
- Pick a real password. The connection string is the only thing
  protecting your library metadata.

Once the container is healthy, verify that the `vector` extension is
available:

```bash
docker exec -it vora-postgres psql -U vora -d vora -c "CREATE EXTENSION IF NOT EXISTS vector;"
```

You should see `CREATE EXTENSION` (or `NOTICE: extension "vector"
already exists`). EF Core will also create the extension on first
migration, but running this once up front confirms the image is wired
up correctly.

### 2. Run the Vora container

Pull the image from GHCR and run it with a connection string pointing
at the Postgres container, a JWT secret for signing auth tokens, and
volume mounts for your media and persistent data. On first start, Vora
will apply all EF Core migrations against the database; subsequent
starts only apply new ones.

Make sure `ASPNETCORE_ENVIRONMENT` is **not** set to `Development`
(it's unset by default, which falls back to `Production` — that's what
you want for a server).

```bash
mkdir -p /srv/vora/data

docker run -d \
  --name vora \
  --restart unless-stopped \
  -p 8080:8080 \
  -e PUID=99 \
  -e PGID=100 \
  -e ASPNETCORE_HTTP_PORTS=8080 \
  -e ConnectionStrings__DefaultConnection="Host=vora-postgres;Port=5432;Database=vora;Username=vora;Password=change-me-to-a-strong-password" \
  -e Jwt__SecretKey="$(openssl rand -base64 64)" \
  -e StoragePaths__CustomArtwork=/app/data/custom_artwork \
  -e StoragePaths__OriginalArtworkCache=/app/data/original_artwork_cache \
  -e StoragePaths__EpgCache=/app/data/iptv/epg_cache \
  -e StoragePaths__IptvDvr=/app/data/iptv/dvr \
  -e StoragePaths__UserImages=/app/data/users \
  -e StoragePaths__Plugins=/app/data/plugins \
  -v /srv/vora/data:/app/data \
  -v /mnt/media/movies:/media/movies:ro \
  -v /mnt/media/shows:/media/shows:ro \
  -v /mnt/media/music:/media/music:ro \
  --link vora-postgres \
  ghcr.io/axufuris/vora-media-server:qa
```

Vora is now reachable at `http://<host>:8080`. The first profile you
create through the setup flow becomes the server admin.

Key environment variables:

- `PUID` / `PGID` — the user and group the container runs as, just
  like Jellyfin, Plex, and the LinuxServer images. On startup, while
  still root, the container ensures a `vora` user/group with these
  ids, recursively chowns the `/app/data` and `/transcode` mounts to
  match, then drops privileges and runs the app as that user. This
  makes bind mounts writable no matter how the host directories are
  owned, with **no host-side `chown` needed**. The library mounts
  under `/media` are **never** chowned — they keep their host
  ownership (mount them `:ro` anyway). Defaults are `99` / `100`
  (Unraid's `nobody:users`); on a plain Linux host set them to your
  own `id -u` / `id -g`. Restarts are idempotent — the ids are
  re-applied cleanly every time. An optional `UMASK` (default `022`)
  controls the permissions of files the app creates.
- `ConnectionStrings__DefaultConnection` — Npgsql connection string.
  Use the **container name** of the Postgres service as `Host` when
  both run on the same Docker network.
- `Jwt__SecretKey` — long random string used to sign profile and
  account tokens. Generate once and keep it stable; rotating it
  invalidates every existing session.
- `StoragePaths__*` — locations inside the container for
  user-uploaded artwork, the original-artwork download cache, the IPTV
  EPG cache, DVR recordings, user profile images, and uploaded
  plugins. Keep them all under one mounted volume so a single backup
  covers everything. Transcoder scratch (HLS segments, IPTV timeshift)
  lives under `/transcode` and is configured separately through the
  admin UI — that path should be a fast local disk or tmpfs.
  The server also writes a **resized-artwork cache** under
  `StoragePaths__CustomArtwork/imagecache` (posters/stills/backdrops
  the clients request through `/api/artwork/thumb`). It self-bounds at
  512 MB and needs no separate volume — see
  [`docs/artwork-image-cache.md`](docs/artwork-image-cache.md).

Media mounts should be read-only (`:ro`) unless you want Vora to
manage the files. Read-only is safer; the API only needs to read.

### docker-compose example

For most self-hosted setups it's easier to run the whole stack from a
single `docker-compose.yml`. The repo includes one tuned for local
development; the snippet below is a server-ready version:

```yaml
services:
  postgres:
    image: pgvector/pgvector:pg16
    container_name: vora-postgres
    restart: unless-stopped
    environment:
      POSTGRES_DB: vora
      POSTGRES_USER: vora
      POSTGRES_PASSWORD: change-me-to-a-strong-password
    volumes:
      - /srv/vora/postgres-data:/var/lib/postgresql/data

  vora:
    image: ghcr.io/axufuris/vora-media-server:qa
    container_name: vora
    restart: unless-stopped
    depends_on:
      - postgres
    ports:
      - "8080:8080"
    environment:
      PUID: 99
      PGID: 100
      ASPNETCORE_HTTP_PORTS: 8080
      ConnectionStrings__DefaultConnection: "Host=postgres;Port=5432;Database=vora;Username=vora;Password=change-me-to-a-strong-password"
      Jwt__SecretKey: "REPLACE_WITH_A_LONG_RANDOM_STRING"
      StoragePaths__CustomArtwork: /app/data/custom_artwork
      StoragePaths__OriginalArtworkCache: /app/data/original_artwork_cache
      StoragePaths__EpgCache: /app/data/iptv/epg_cache
      StoragePaths__IptvDvr: /app/data/iptv/dvr
      StoragePaths__UserImages: /app/data/users
      StoragePaths__Plugins: /app/data/plugins
    volumes:
      - /srv/vora/data:/app/data
      - /srv/vora/transcode:/transcode
      - /mnt/media/movies:/media/movies:ro
      - /mnt/media/shows:/media/shows:ro
      - /mnt/media/music:/media/music:ro
```

Bring it up with `docker compose up -d`. Vora will wait for the
Postgres container to start, apply migrations, and then begin serving.

### Optional: NVIDIA GPU passthrough

If you want hardware-accelerated transcoding, install the NVIDIA
Container Toolkit on the host and add a `deploy` block to the `vora`
service:

```yaml
    deploy:
      resources:
        reservations:
          devices:
            - driver: nvidia
              count: 1
              capabilities: [gpu, video]
```

The image ships with `NVIDIA_VISIBLE_DEVICES=all` and
`NVIDIA_DRIVER_CAPABILITIES=all` baked in, so on any host that launches
it with the NVIDIA runtime, `libnvidia-container` injects `libcuda.so.1`
plus the NVENC/NVDEC and Vulkan/OpenCL driver libraries — CUDA decode,
NVENC encode, and the `tonemap_cuda` HDR→SDR filter all work out of the
box. **The host only needs the NVIDIA Container Toolkit runtime
enabled.** Don't override `NVIDIA_DRIVER_CAPABILITIES` with a narrower
value (e.g. `utility`) — that omits `libcuda.so.1` and you'll see
`Could not dynamically load CUDA` / `Cannot load libcuda.so.1` when a
transcode starts.

### Unraid

Unraid users typically install both containers from **Apps → Add
Container** (Community Applications) using a manual template. Both
templates need the **Network Type** set to the same custom Docker
network so the Vora container can resolve Postgres by its container
name. Create one first if you don't already have a user-defined
bridge:

```
Settings → Docker → Add Network → bridge → name it "vora"
```

Then add each container. The fields below assume the Unraid array is
mounted at `/mnt/user` and that your media lives under
`/mnt/user/media`.

**Postgres container**

| Field | Value |
| --- | --- |
| Name | `vora-postgres` |
| Repository | `pgvector/pgvector:pg16` |
| Network Type | `vora` (the custom bridge above) |
| Console shell | `Bash` |
| Restart Policy | `unless-stopped` |
| Path: `/var/lib/postgresql/data` | `/mnt/user/appdata/vora-postgres` |
| Variable: `POSTGRES_DB` | `vora` |
| Variable: `POSTGRES_USER` | `vora` |
| Variable: `POSTGRES_PASSWORD` | a strong password you generate |

Leave the port unmapped — keeping Postgres off the LAN is safer, and
the Vora container will reach it over the internal Docker network.
Apply, then wait for the container to go green in the Docker tab.

**Vora container**

| Field | Value |
| --- | --- |
| Name | `vora` |
| Repository | `ghcr.io/axufuris/vora-media-server:qa` |
| Network Type | `vora` (same custom bridge as Postgres) |
| Restart Policy | `unless-stopped` |
| Port: container `8080` | host `8080` (or any free port) |
| Variable: `PUID` | `99` (Unraid's `nobody`) |
| Variable: `PGID` | `100` (Unraid's `users`) |
| Path: `/app/data` | `/mnt/user/appdata/vora` |
| Path: `/media/movies` | `/mnt/user/media/movies` — Access Mode `Read Only` |
| Path: `/media/shows` | `/mnt/user/media/shows` — Access Mode `Read Only` |
| Path: `/media/music` | `/mnt/user/media/music` — Access Mode `Read Only` |
| Variable: `ASPNETCORE_HTTP_PORTS` | `8080` |
| Variable: `ConnectionStrings__DefaultConnection` | `Host=vora-postgres;Port=5432;Database=vora;Username=vora;Password=<the password from above>` |
| Variable: `Jwt__SecretKey` | a long random string (e.g. output of `openssl rand -base64 64` from any shell) |
| Variable: `StoragePaths__CustomArtwork` | `/app/data/custom_artwork` |
| Variable: `StoragePaths__OriginalArtworkCache` | `/app/data/original_artwork_cache` |
| Variable: `StoragePaths__EpgCache` | `/app/data/iptv/epg_cache` |
| Variable: `StoragePaths__IptvDvr` | `/app/data/iptv/dvr` |
| Variable: `StoragePaths__UserImages` | `/app/data/users` |
| Variable: `StoragePaths__Plugins` | `/app/data/plugins` |
| Path: `/transcode` | `/mnt/user/appdata/vora-transcode` (or a fast scratch disk) |

`PUID=99` / `PGID=100` are Unraid's `nobody:users`, which own
everything under `/mnt/user` by default — so Vora writes to its
`appdata` and `transcode` shares out of the box with no manual
`chmod`/`chown`. They're already the container defaults; the rows
above just make them visible and editable on the template. If your
shares are owned by a different user, set these to that user's id
instead.

Apply. Vora will auto-migrate the database on first launch (watch the
container log if you want to confirm — you should see
`Applying N pending database migration(s)`). Once it's up, open
`http://<unraid-ip>:8080` and create the first profile, which becomes
the server admin.

For NVIDIA GPU transcoding on Unraid, install the **Nvidia-Driver**
plugin from CA, then on the Vora container template set **Extra
Parameters** to `--runtime=nvidia`. That's the only change required —
the image already carries `NVIDIA_VISIBLE_DEVICES=all` and
`NVIDIA_DRIVER_CAPABILITIES=all`, so you do **not** need to add those
variables by hand (Unraid's `--runtime=nvidia` path ignores the compose
`deploy` capabilities list, which is why baking them into the image is
what makes CUDA work here). To pin a specific GPU, you can still add a
`NVIDIA_VISIBLE_DEVICES` variable set to its UUID from `nvidia-smi`, but
leave `NVIDIA_DRIVER_CAPABILITIES` alone — narrowing it drops
`libcuda.so.1` and breaks transcoding.

### Upgrading

When a new image is published, pull it and restart the Vora container.
On startup it detects any new EF Core migrations and applies them
automatically before serving traffic; your `/app/data` volume and the
Postgres data directory persist across upgrades. If a migration fails,
the container exits without serving traffic — check the logs, fix the
issue, and restart.

## Bootstrapping plugin API keys from environment variables

You can supply plugin API keys (and any other plugin setting) as
environment variables in your `docker run` / `docker-compose.yml`.
On startup Vora reads them, validates that the plugin and setting key
both exist, and writes the value into the database — but **only when
the row is empty**. Once a value lives in the database, the seeder
leaves it alone so changes you make later through the admin UI are
preserved across container restarts.

### Format

```
Vora__PluginSettings__<pluginId>__<settingKey>=<value>
```

The double underscores are the standard .NET configuration separator;
they map to the `Vora:PluginSettings:<pluginId>:<settingKey>` config
path inside the app. The `<pluginId>` matches the plugin's internal
id (see the table below), and `<settingKey>` is one of the setting
keys that plugin defines.

Every plugin also responds to a special `is_enabled` key, so you can
enable or disable a plugin from compose:

```
Vora__PluginSettings__openai_recommendations__is_enabled=true
```

### What gets seeded, what doesn't

The seeder runs once per container start, after database migrations
and before workers come up. For each environment variable it sees, it:

1. **Verifies the plugin exists.** If `<pluginId>` doesn't match an
   installed plugin, the variable is skipped with a warning in the
   logs:
   `WARN  Plugin settings environment variable references plugin 'foo', but no plugin with that id is installed — skipping.`
2. **Verifies the setting key exists.** If `<settingKey>` isn't a
   recognized setting for that plugin (and isn't `is_enabled`), it's
   skipped with:
   `WARN  Plugin 'tmdb_metadata' has no setting named 'bogus_key' — skipping seed value.`
3. **Checks the database.** If the setting already has a non-empty
   value in the database, the env var is ignored — admin UI edits win.
   The seed only fires when the row is missing or blank.
4. **Redacts values in logs.** The seeder logs which keys it wrote
   (`fanart_artwork.api_key`, `tmdb_metadata.api_key`, …) but never
   the values themselves, so API keys never end up in your log files
   or the in-app log viewer.

### Plugins and their settings

The table below lists every plugin shipped with Vora and the settings
it accepts. Every plugin also supports `is_enabled` (`true` / `false`)
in addition to the keys shown.

| Plugin id | Plugin name | Settings |
| --- | --- | --- |
| `tmdb_metadata` | The Movie Database (TMDB) | `api_key` |
| `tmdb_discovery` | TMDB Discovery Engine | `discovery_region`, `discovery_language` |
| `tmdb_artwork` | TMDB Artwork | — |
| `tvdb_metadata` | The TV Database (TVDB) | `api_key` |
| `tvdb_artwork` | TVDB Artwork | — |
| `omdb_imdb` | OMDb — IMDb Ratings | `api_key` |
| `fanart_artwork` | Fanart.tv Artwork | `api_key` |
| `fanart_music_artwork` | Fanart.tv Music Artwork | — |
| `mediux_artwork` | MediUX Artwork | `api_key` |
| `theaudiodb_artwork` | TheAudioDB Artwork | `api_key` |
| `musicbrainz_artwork` | MusicBrainz Artwork | — |
| `mal_artwork` | MyAnimeList Artwork | `client_id` |
| `mal_discovery` | MyAnimeList Discovery Engine | — |
| `lastfm_listening` | Last.fm Scrobbling | `api_key`, `api_secret` |
| `genius_lyrics` | Genius Lyrics | `access_token` |
| `lrclib_lyrics` | LRClib Lyrics | — |
| `serpapi_theater` | SerpApi Google Showtimes | `api_key`, `default_location`, `max_theaters`, `auto_showtimes` |
| `radarr_calendar` | Radarr Calendar | — *(reads from Request Servers tagged "Use for Release Calendar")* |
| `radarr_requester` | Radarr | — *(uses Request Servers admin page)* |
| `sonarr_calendar` | Sonarr Calendar | — *(reads from Request Servers tagged "Use for Release Calendar")* |
| `sonarr_requester` | Sonarr | — *(uses Request Servers admin page)* |
| `trakt_collection_sync` | Trakt.tv Lists | `client_id` |
| `trakt_chronology` | Trakt.tv Community Lists | — |
| `mdblist_collection_sync` | MDbList.com | `api_key` |
| `mdblist_chronology` | MDbList.com Timelines | — |
| `imdb_collection_sync` | IMDb Public Lists | — |
| `imdb_chronology` | IMDB Community Lists | — |
| `itunes_podcast_discovery` | iTunes Podcast Discovery | — |
| `openai_recommendations` | OpenAI Smart Recommendations | `api_key`, `chat_model`, `schedule_time` |
| `local_imagesharp_overlays` | Vora Native Overlays | `enable_schedule`, `schedule_time` |
| `local_metadata` | Local Assets (NFO & Images) | — |
| `local_artwork` | Local Assets Artwork | — |
| `local_recommendations` | Vora Local Recommendations | — |
| `local_calendar` | Vora Local Calendar | — |
| `Vora_scanner` | Vora Standard Scanner | — |
| `polling_watcher` | Polling Watcher | — |
| `native_watcher` | Native OS Watcher | — |

### Full docker-compose example

A realistic environment block prefilling the common third-party
provider keys:

```yaml
    environment:
      ASPNETCORE_HTTP_PORTS: 8080
      ConnectionStrings__DefaultConnection: "Host=postgres;Port=5432;Database=vora;Username=vora;Password=change-me"
      Jwt__SecretKey: "REPLACE_WITH_A_LONG_RANDOM_STRING"

      # Storage
      StoragePaths__CustomArtwork: /app/data/custom_artwork
      StoragePaths__OriginalArtworkCache: /app/data/original_artwork_cache
      StoragePaths__EpgCache: /app/data/iptv/epg_cache
      StoragePaths__IptvDvr: /app/data/iptv/dvr
      StoragePaths__UserImages: /app/data/users
      StoragePaths__Plugins: /app/data/plugins

      # Metadata + artwork
      Vora__PluginSettings__tmdb_metadata__api_key: "YOUR_TMDB_API_KEY"
      Vora__PluginSettings__tvdb_metadata__api_key: "YOUR_TVDB_API_KEY"
      Vora__PluginSettings__omdb_imdb__api_key: "YOUR_OMDB_API_KEY"
      Vora__PluginSettings__fanart_artwork__api_key: "YOUR_FANART_KEY"
      Vora__PluginSettings__mediux_artwork__api_key: "YOUR_MEDIUX_KEY"
      Vora__PluginSettings__theaudiodb_artwork__api_key: "YOUR_AUDIODB_KEY"
      Vora__PluginSettings__mal_artwork__client_id: "YOUR_MAL_CLIENT_ID"

      # Discovery tuning
      Vora__PluginSettings__tmdb_discovery__discovery_region: "US"
      Vora__PluginSettings__tmdb_discovery__discovery_language: "en-US"

      # Lyrics + scrobbling
      Vora__PluginSettings__lastfm_listening__api_key: "YOUR_LASTFM_KEY"
      Vora__PluginSettings__lastfm_listening__api_secret: "YOUR_LASTFM_SECRET"
      Vora__PluginSettings__genius_lyrics__access_token: "YOUR_GENIUS_TOKEN"

      # Movie showtimes
      Vora__PluginSettings__serpapi_theater__api_key: "YOUR_SERPAPI_KEY"
      Vora__PluginSettings__serpapi_theater__default_location: "90210"
      Vora__PluginSettings__serpapi_theater__max_theaters: "10"
      Vora__PluginSettings__serpapi_theater__auto_showtimes: "true"

      # Trakt / MDbList list sync
      Vora__PluginSettings__trakt_collection_sync__client_id: "YOUR_TRAKT_CLIENT_ID"
      Vora__PluginSettings__mdblist_collection_sync__api_key: "YOUR_MDBLIST_KEY"

      # OpenAI smart recommendations (opt-in)
      Vora__PluginSettings__openai_recommendations__is_enabled: "true"
      Vora__PluginSettings__openai_recommendations__api_key: "YOUR_OPENAI_KEY"
      Vora__PluginSettings__openai_recommendations__chat_model: "gpt-4o-mini"
      Vora__PluginSettings__openai_recommendations__schedule_time: "02:00"

      # Local poster overlays
      Vora__PluginSettings__local_imagesharp_overlays__enable_schedule: "true"
      Vora__PluginSettings__local_imagesharp_overlays__schedule_time: "03:00"
```

Only set the keys you actually use — the seeder simply does nothing
for plugins you don't reference. If you later add a new key to your
compose file and rebuild, it will be picked up on the next start
*provided* the database row is still empty for that key. If you've
already saved a value through the admin UI, you can clear it in the
UI to let the env var take effect on the next start, or just continue
to manage it through the UI from then on.

## Documentation

Project documentation lives under [`docs/`](docs/). Highlights:

- [`docs/architecture.md`](docs/architecture.md) — solution layout and
  dependency rules
- [`docs/backend-conventions.md`](docs/backend-conventions.md) — API
  structure and C# conventions
- [`docs/frontend-conventions.md`](docs/frontend-conventions.md) — React
  app structure and conventions
- [`docs/database.md`](docs/database.md) — schema, migrations, vector
  extension
- [`docs/scanning-and-tasks.md`](docs/scanning-and-tasks.md) — media
  ingest, background tasks, parallel per-unit scan+enrich, exclude
  filters, and Media Trash
- [`docs/artwork-image-cache.md`](docs/artwork-image-cache.md) — resized
  artwork cache and poster overlay badges
- [`docs/auth-and-devices.md`](docs/auth-and-devices.md) — auth flow and
  device tracking
- [`docs/iptv-and-dvr.md`](docs/iptv-and-dvr.md) — IPTV, EPG, and DVR
  architecture
- [`docs/music-and-audio.md`](docs/music-and-audio.md) — music subsystem
- [`docs/playlists.md`](docs/playlists.md) — manual and smart playlists
- [`docs/collections.md`](docs/collections.md) — collection content
  sync, the AI List (franchise/universe) provider, and AI chronological
  ordering
- [`docs/redesign/`](docs/redesign/) — client templates, scheduling, and
  design language

## License

Vora is released under the **GNU Affero General Public License v3.0**.
See [`LICENSE`](LICENSE) for the full text.

AGPL-3.0 means you are free to use, study, modify, and distribute Vora,
including in commercial settings — but if you run a modified version
that interacts with users over a network, you must make the modified
source code available to those users under the same license.

## Trademark

"Vora" and the Vora logo are trademarks of **Andreas (Andy) Xufuris**.

The AGPL-3.0 license grants you broad rights to use, modify, and
redistribute the Vora source code, but it does **not** grant any right
to use the Vora name, logo, or other brand assets. If you fork this
project or build a derivative work, you must use a different name and
visual identity. You may state factually that your project is "based on
Vora" or "a fork of Vora," but you may not present your fork as Vora
itself or use the Vora marks in a way that suggests endorsement.

Use of the Vora name and marks in any commercial context — including
hosted services, distributions, or merchandise — requires prior written
permission.

See [`TRADEMARK.md`](TRADEMARK.md) for the full trademark policy.
