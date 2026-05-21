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

Apply. Vora will auto-migrate the database on first launch (watch the
container log if you want to confirm — you should see
`Applying N pending database migration(s)`). Once it's up, open
`http://<unraid-ip>:8080` and create the first profile, which becomes
the server admin.

For NVIDIA GPU transcoding on Unraid, install the **Nvidia-Driver**
plugin from CA, then on the Vora container template:

- Set **Extra Parameters** to `--runtime=nvidia`.
- Add a variable `NVIDIA_VISIBLE_DEVICES` with value `all` (or a
  specific GPU UUID from `nvidia-smi`).
- Add a variable `NVIDIA_DRIVER_CAPABILITIES` with value `all`.

### Upgrading

When a new image is published, pull it and restart the Vora container.
On startup it detects any new EF Core migrations and applies them
automatically before serving traffic; your `/app/data` volume and the
Postgres data directory persist across upgrades. If a migration fails,
the container exits without serving traffic — check the logs, fix the
issue, and restart.

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
- [`docs/auth-and-devices.md`](docs/auth-and-devices.md) — auth flow and
  device tracking
- [`docs/iptv-and-dvr.md`](docs/iptv-and-dvr.md) — IPTV, EPG, and DVR
  architecture
- [`docs/music-and-audio.md`](docs/music-and-audio.md) — music subsystem
- [`docs/playlists.md`](docs/playlists.md) — manual and smart playlists
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
