# Backend conventions

How `Vora.Api`, `Vora.Application`, and `Vora.Infrastructure` are structured day to day.

## `Vora.Api` layout

The Api project is intentionally organized so `Program.cs` stays tiny (≈14 lines) and every concern lives in its own well-named file.

### `Vora.Api/Program.cs`

Just builds the host, applies the Vora extension methods, runs startup tasks, calls `app.Run()`. **Never** add inline DI or pipeline code here.

### `Vora.Api/Extensions/` (composition root)

- **`ServiceRegistrationExtensions.cs`** — `builder.AddVoraServices()` and its helpers. One helper per concern: `AddVoraDatabase`, `AddVoraRepositories`, `AddVoraManagers`, `AddVoraApplicationServices`, `AddVoraWorkers`, `AddVoraInfrastructure`, `AddVoraRealtime`, `AddVoraPluginSystem`, `AddVoraAuthenticationAndAuthorization`, `AddVoraCors`, `AddVoraJsonOptions`. When adding a new manager/service/repository, register it in the helper that matches its kind.
- **`WebApplicationExtensions.cs`** — `app.UseVoraPipeline()` (middleware order) and `app.MapVoraEndpoints()` (the alphabetized list of `Map*Endpoints` calls). New endpoint registration goes here.
- **`StartupTaskExtensions.cs`** — `app.RunVoraStartupTasksAsync()`. Folder watcher init + IPTV EPG cache preload. New one-shot startup work goes here.
- **`AuthExtensions.cs`** — `ClaimsPrincipal` extension methods: `GetProfileId`, `GetAccountId`, `HasAllLibraryAccess`, `GetAllowedLibraryIds`, `HasAllContentRatings`, `GetAllowedContentRatings`, `BlockUnratedContent`. **Always** use these instead of re-parsing claims inline.
- **`PluginLoaderExtensions.cs`** — `services.AddVoraPlugins(path)`. Scans built-in and external assemblies and registers any concrete `IVoraPlugin`. New plugin provider interfaces must be added to the `PluginProviderInterfaces` array.
- **`PluginSettingsAdapter.cs`** — adapts `Vora.Application.Settings.ISystemSettingsRepository` to the plugin-facing `IPluginSettingsProvider`.

### `Vora.Api/Endpoints/`

One file per resource (or per resource + admin variant). Each file is a static class named `XxxEndpoints` exposing `MapXxxEndpoints` on `IEndpointRouteBuilder`. The shape:

```csharp
using ...; // System first, then Microsoft, then Vora.* alphabetical

namespace Vora.Api.Endpoints;

public class MyRequestDto { ... }    // Inbound DTOs declared above the static class

public static class MyEndpoints
{
    public static RouteGroupBuilder MapMyEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/...").WithTags("...").RequireAuthorization(...);

        group.MapGet("/", GetXxxAsync);          // Reads first
        group.MapPost("/", CreateXxxAsync);      // Then writes (POST → PUT → DELETE)
        ...

        return group;
    }

    private static async Task<IResult> GetXxxAsync(...) { ... }
    // Handler methods below the route table, in the same logical order
}
```

When one file needs to mix client and admin concerns, split into two top-level helpers (`MapClientEndpoints`, `MapAdminEndpoints`) each with its own `RouteGroupBuilder` and auth policy. See `LibraryEndpoints`, `MediaEndpoints`, `RequestEndpoints`, `SmartListEndpoints`, `UserEndpoints` for the pattern.

### `Vora.Api/Hubs/`

- **`VoraHub.cs`** — the `Hub` itself. Mapped at `/hubs/Vora`.
- **`SignalRClientNotifier.cs`** — implements `Vora.Application.Analysis.IClientNotifier`. Add new push-notification methods here whenever you add events to `IClientNotifier`. See `docs/realtime.md`.

### `Vora.Api/Middleware/`

- **`DeviceTrackingMiddleware.cs`** — extracts the `X-Vora-Device-Id` header on every authenticated request, upserts the `ClientDevice` row, geo-locates new IPs via `IHttpClientFactory` (named client `DeviceTrackingMiddleware.GeoLookupHttpClientName`). Never add a static `HttpClient`. See `docs/auth-and-devices.md`.

## Endpoint authentication policy

Every endpoint must declare its auth requirement explicitly. The two policies:

- **`RequireAuthorization("AdminOnly")`** — server administration. The `AdminOnly` policy requires the `IsAdmin` claim with value `True`.
- **`RequireAuthorization()`** — any authenticated user.

**Public exceptions** (via `.AllowAnonymous()` or by leaving the route off any auth group):

- `AuthEndpoints` — `setup-status`, `setup`, `login`, `register`, `exchange-profile-token`
- `ArtworkEndpoints` — `GET /api/artwork/custom/{fileName}` (image tags can't carry a JWT)
- `UserImageEndpoints` — `GET /api/users/images/custom/{fileName}` (same reason)
- `StreamingEndpoints` — `GET /api/streaming/play/{sessionId}` and `GET /api/streaming/hls/{fileName}` (the `<video>` element fetches these directly; auth is implicit via short-lived session token in the URL)
- `DvrPlaybackEndpoints` — `GET /api/streaming/dvr/file/{sessionId}` and `GET /api/streaming/hls/timeshift/...` (same reason)

When adding a new endpoint, default to `RequireAuthorization()`. Lock it to `AdminOnly` if it administers server-wide state (libraries, IPTV playlists/EPG sources, plugins, settings, tasks queue, smart-list management, request approval, user access changes, dedupe).

## C# style

- **Nullable reference types: ON.** Treat warnings as bugs. Fix them; do not suppress with `!` or `#pragma`.
- **Implicit usings: ON.**
- **Async methods always use the `Async` suffix.**
- **Naming:** `IFooRepository` / `FooRepository`, `IFooManager` / `FooManager`, `IFooService` / `FooService`.
- **No comments.** The codebase is intentionally comment-free — let names and structure carry meaning. XML doc comments are also off. If a method or class needs explanation, that's a signal to rename or restructure.

## Layered placement rules

- **Repository interfaces** live in a separate file *and* a separate project (`Vora.Application`) from their implementations (`Vora.Infrastructure`).
- **Manager and Service interfaces** live at the top of the same file as their implementation (both inside `Vora.Application`).
- **DI registration** lives in the relevant helper inside `Vora.Api/Extensions/ServiceRegistrationExtensions.cs`. Never inline a registration in `Program.cs`.
- **Endpoints stay thin.** Each minimal-API handler is a private static method on the endpoint class; the route group is just a table of `MapGet/Post/Put/Delete → handler` lines. Heavy logic goes into a Manager.

## Claims, HTTP, errors, responses

- **Claims access goes through `AuthExtensions`.** Don't call `user.FindFirst("...")` directly inside a handler. If you need a new claim shape, add a helper there.
- **HTTP clients use `IHttpClientFactory`.** No static `HttpClient` fields. Name your clients with a public `const` (e.g. `DeviceTrackingMiddleware.GeoLookupHttpClientName`).
- **Error / exception strategy:** log the error and **then throw**, so middleware can surface a useful message to the frontend. Don't swallow exceptions.
- **API response shape:** return `*VM` or `*Response`. **Never** expose `Vora.Domain` entities through the API. If you find an endpoint returning an entity, that's a bug — replace it with a VM or create one.

## JSON enum serialization

All enums are written and read as **strings** at the HTTP boundary. This is configured globally via `JsonStringEnumConverter` inside `AddVoraJsonOptions` (`ServiceRegistrationExtensions.cs`):

```csharp
options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
```

That means new enums on request DTOs, VMs, and response shapes round-trip as strings automatically — `"mediaType": "Music"`, not `"mediaType": 1`. The frontend's TypeScript string-union types (`'Music' | 'Movies' | …`) line up directly. Don't reach for `[JsonConverter(typeof(JsonStringEnumConverter))]` on individual enums unless you have a very specific reason — the global converter already covers them.

Common gotcha: if a request still comes back as `JsonException: The JSON value could not be converted to <Enum>`, the running container hasn't been rebuilt after a code change. Rebuild + restart.

## Registry-backed modules

Some modules aggregate items from multiple sources (built-in + plugin-provided) behind a single interface. The pattern is:

- An `IXxxRegistry` (Singleton) holds the catalog and supports `GetAll() / Get(id) / Exists(id)`.
- An `IXxxManager` (Scoped) layers persistence + business logic on top of the registry, including notifying the frontend via `IClientNotifier` when the active selection changes.
- An optional `IXxxBundleLoader` (Singleton, constructor-scans on startup) populates the registry from filesystem bundles, with a `Refresh()` method for hot-reload.

`Vora.Application/Themes/` is the reference implementation:

- `IThemeRegistry` / `ThemeRegistry` — aggregates built-in `ThemeMetaVM`s with bundle metas from the loader.
- `IThemeBundleLoader` / `ThemeBundleLoader` — scans `<install>/Themes/<id>/manifest.json` on construction, validates, exposes `Refresh()` for the admin "Re-scan bundles" action.
- `IThemeAssetService` / `ThemeAssetService` — resolves asset paths inside a bundle with path-traversal protection.
- `IThemeManager` / `ThemeManager` — reads/writes `ServerSetting.AdminThemeId`, fires `IClientNotifier.NotifyAdminThemeChangedAsync` so connected admins re-apply live.

Reach for this shape when you need a new "things admins can pick from, including ones plugins ship" feature.

See also `docs/database.md` for the DbContext layout and migration workflow, and `docs/architecture.md` for the type/folder rules in `Vora.Application` (`*VM` / `*Dto` / `*Request` / `*Response`).
