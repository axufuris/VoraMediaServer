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

Vora is distributed as a Docker image at `ghcr.io/<owner>/vora`. The
container serves both the API and the bundled React client on port
`8080`.

A `docker-compose.yml` is included for local and self-hosted deployment.
See the file for an example configuration including media volume mounts
and NVIDIA GPU passthrough.

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
