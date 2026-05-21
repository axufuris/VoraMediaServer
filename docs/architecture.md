# Architecture

Vora follows a layered/clean-architecture-style design. Domain at the core, Application around it, Infrastructure and Api on the outside, with a separate React frontend and a runtime plugin system.

## Solution layout

All source lives under `/src`. The solution is `Vora.slnx` at the repo root.

### Backend projects

- **`/src/Vora.Api`** — Entry point. Minimal API endpoints. Stays thin: parse requests, call into Managers/Services, return responses. See `docs/backend-conventions.md` for the endpoint pattern.

- **`/src/Vora.Application`** — Business logic layer:
  - **Managers** and **Services** (the actual logic)
  - **Repository interfaces** (defined here so other projects depend on abstractions)
  - **View Models** consumed by the frontend (`*VM`)
  - **DTOs** crossing layers (`*Dto`)
  - **Requests** (inbound API bodies, `*Request`) and **Responses** (`*Response`)

- **`/src/Vora.Domain`** — Pure domain library:
  - Database **entities**
  - **Enums**
  - No dependencies on other projects in the solution.

- **`/src/Vora.Infrastructure`** — Database and external-system code:
  - **Repository implementations** (fulfill interfaces from `Vora.Application`)
  - **EF Core DbContext** (split into partials and Configure* helpers)
  - **FFmpeg analyzer**, filesystem analyzers
  - **Workers** (background hosted services)
  - **Migrations** in `/Migrations`

### Plugin system

- **`/src/plugins/Vora.Plugins.Abstractions`** — Interfaces and DTOs that every plugin implements/uses. The only plugin-system dependency concrete plugins should have (besides `Vora.Domain` if entities are needed).
- **`/src/plugins/*`** — Individual plugin implementations. Each references `Vora.Plugins.Abstractions`. See `docs/plugins.md`.

### Frontend

- **`/src/Vora.Web`** — React + TypeScript SPA. Consumes the API exposed by `Vora.Api`. View Model shapes come from `Vora.Application`. See `docs/frontend-conventions.md`.

### Infrastructure / orchestration

- **`/docker-compose.dcproj`** — Visual Studio Docker Compose project (startup project).
- **`/docker-compose.yml`** — services, images, networks, volumes.
- **`/Dockerfile`** — API image (includes FFmpeg).

## Dependency rules

| Project | Depends on |
| --- | --- |
| `Vora.Domain` | nothing in the solution |
| `Vora.Application` | `Vora.Domain` |
| `Vora.Infrastructure` | `Vora.Application`, `Vora.Domain` |
| `Vora.Api` | `Vora.Application` (+ `Vora.Infrastructure` for DI wire-up) |
| `Vora.Plugins.Abstractions` | nothing concrete (only `Vora.Domain` if entities are needed) |
| individual plugins | `Vora.Plugins.Abstractions` (+ `Vora.Domain` if needed) |
| `Vora.Web` | independent — talks to `Vora.Api` over HTTP only |

If you ever feel like adding a reference that breaks this graph, stop and restructure.

## Where to put new code

| Kind of change | Where it goes | Register it in |
| --- | --- | --- |
| New API endpoint | `Vora.Api/Endpoints/` | `WebApplicationExtensions.MapVoraEndpoints` |
| New Manager or Service | `Vora.Application` (interface at top of file with impl) | `ServiceRegistrationExtensions.AddVoraManagers` / `AddVoraApplicationServices` |
| New entity or enum | `Vora.Domain` | — |
| New repository | interface in `Vora.Application`, impl in `Vora.Infrastructure` | `ServiceRegistrationExtensions.AddVoraRepositories` |
| New view model for the UI | `Vora.Application` | — |
| New DTO crossing layers | `Vora.Application` | — |
| New Request / Response shape | `Vora.Application` | — |
| New background worker | `Vora.Infrastructure` | `ServiceRegistrationExtensions.AddVoraWorkers` |
| New media or filesystem analyzer | `Vora.Infrastructure` | — |
| New plugin contract | `Vora.Plugins.Abstractions` | also add to `PluginLoaderExtensions.PluginProviderInterfaces` |
| New plugin implementation | new project under `/src/plugins/` | — |
| New React component / page / hook | `Vora.Web` (see `docs/frontend-conventions.md` for folder picking) | — |
| New SignalR notification | method on `IClientNotifier` in `Vora.Application.Analysis`, then implement in `SignalRClientNotifier` (`Vora.Api/Hubs/`) | — |
| New feature toggle | add a `bool EnableX` to `ServerSetting`, mirror in `FeatureFlagsVM` + `UpdateFeatureFlagsRequest`, add to `FeatureGate` enum and `RequireFeatureFilter`, mirror in `FeatureFlagsVM` on the frontend, then gate the relevant endpoints with `.RequireFeature(FeatureGate.X)` and the nav with `isNavItemEnabled` in `MainLayout.tsx` | run `add-migration AddXFeatureToggle` |
| New email template | add an enum value to `Vora.Domain.Enums.EmailTemplateKey`; add three files under `Vora.Application/Email/Templates/` (`<Key>.subject.txt`, `.html`, `.txt`); mark each as `<EmbeddedResource>` in `Vora.Application.csproj`; add a variable list to `EmailTemplateVariables.Catalog`; add display name + description to `EmailTemplateManager.Metadata`. Send via `IEmailService.SendAsync(new EmailMessage { ... })`. See `docs/email.md`. | — |
| New email transport (e.g. SendGrid plugin) | implement `Vora.Application.Email.IEmailTransport`. The default impl is `SmtpEmailTransport` in `Vora.Infrastructure/Email/`. | replace registration in `AddVoraEmail` inside `ServiceRegistrationExtensions` |
| New admin feature page | `pages/Admin/Features/<Name>Page.tsx` — use `FeatureToggle` + `FeaturePluginList` from `components/Admin/Features/`. Add the route in `App.tsx` (mirror under `/server/:serverId/admin/...`) and **one entry** in `components/Admin/Shell/adminNavData.tsx` (the sidebar AND the Cmd-K palette both consume that list) | — |
| New admin sidebar / palette entry | `components/Admin/Shell/adminNavData.tsx` — single source of truth for both `SidebarV2` and `SearchPalette`. Set `section`, `icon`, optional `keywords` for palette fuzzy-match, optional `requires: 'ai'` runtime gate | — |
| New built-in admin theme | `Vora.Web/src/theme/themes/<id>.ts` (frontend manifest) **and** add a `ThemeMetaVM` entry in `Vora.Application/Themes/IThemeRegistry.cs` so the backend picker knows about it. Register the manifest in `BUILT_IN_THEMES` in `theme/ThemeProvider.tsx` and add a swatch row in `AppearancePage`'s `INACTIVE_SWATCHES`. Plugin-shipped themes don't need any of this — they go in `<install>/Themes/<id>/` and are scanned on startup (see `docs/admin-theme-bundles.md`) | — |

## Type naming and folder rules in `Vora.Application`

These four kinds of types each live in their own folder and are never mixed:

| Suffix | Purpose | Example |
| --- | --- | --- |
| `*VM` | View Model returned from API endpoints | `UserVM`, `VideoLibraryVM` |
| `*Dto` | Moving data between layers internally | `UserDto` |
| `*Request` | Inbound API request body | `CreateUserRequest` |
| `*Response` | Outbound API response when a VM alone isn't the right shape | `CreateUserResponse` |

Don't reuse one as another — e.g. never return a `*Dto` from an endpoint, return a `*VM` or a `*Response`.

## Build & run

This solution is run from Visual Studio 2026 — there is no CLI build to invoke from outside.

1. If `Vora.Web` isn't already running, open a terminal in `/src/Vora.Web` and run `npm run dev`.
2. In Visual Studio, set `docker-compose.dcproj` as the startup project and run it.

The API **always** runs in Docker (FFmpeg lives inside the container). Don't try to run `Vora.Api` natively.

Ports are baked into the project configs — don't override them.
