# OpenAPI codegen for native clients

Every native client (Android, Apple, future Roku) consumes a generated typed API client built from `Vora.Api`'s OpenAPI document. The backend is the single source of truth; client SDKs are regenerated on demand. No hand-maintained DTOs, no manually-typed endpoints.

This file covers: how the OpenAPI doc is produced, how to harden it for codegen, and concrete commands for generating Swift and Kotlin clients. The platform decision rationale is in [`docs/adr/0002-client-platform-strategy.md`](../adr/0002-client-platform-strategy.md).

## How `Vora.Api` exposes the OpenAPI document

`AddVoraSwagger()` (in `Vora.Api/Extensions/ServiceRegistrationExtensions.cs`) wires up Swashbuckle:

```csharp
private static IServiceCollection AddVoraSwagger(this IServiceCollection services)
{
    services.AddEndpointsApiExplorer();
    services.AddSwaggerGen();
    return services;
}
```

`UseVoraPipeline()` mounts `UseSwagger()` and `UseSwaggerUI()` only when `app.Environment.IsDevelopment()`. The doc is served at `/swagger/v1/swagger.json`; the UI at `/`.

That's sufficient for now: codegen runs against a development instance of `Vora.Api`. Production builds don't need the Swagger UI mounted.

## Hardening Swashbuckle for codegen

The default `AddSwaggerGen()` call produces an OpenAPI document that *works* for codegen but generates ugly client method names and loses some type fidelity. The recommended hardening (do this before scaling out to a second client) is to:

### 0. Keep `Vora.Plugins` types out of the schema

Every `.Produces<T>()` must name a `*VM` / `*Response` from `Vora.Application`. A `Vora.Plugins` DTO returned directly gets pulled into the OpenAPI document, and from there into every generated Swift/Kotlin client — so plugin-assembly types become part of the public client surface and a plugin-side refactor silently breaks native builds.

This is the project-wide View Model rule (see `docs/backend-conventions.md`), but codegen is what makes violating it expensive. `GET /api/discovery/details/{providerId}/{type}/{externalId}` used to return `DiscoveryItemDetailsDto` straight from `Vora.Plugins`; it now returns `DiscoveryItemDetailsVM`. When adding an endpoint, check the `.Produces<>` type's namespace.

### 1. Set explicit operation IDs

Without explicit IDs, Swashbuckle generates names like `GetApiUsersById` from verb + route. With explicit IDs, generated method names match what the endpoint actually does.

Add to `Vora.Api/Extensions/ServiceRegistrationExtensions.cs`:

```csharp
private static IServiceCollection AddVoraSwagger(this IServiceCollection services)
{
    services.AddEndpointsApiExplorer();
    services.AddSwaggerGen(options =>
    {
        options.CustomOperationIds(api =>
        {
            // Endpoint name set via .WithName("...") on each endpoint definition.
            // Falls back to "{Controller}_{Action}" shape when not set.
            return api.ActionDescriptor.AttributeRouteInfo?.Name
                ?? $"{api.ActionDescriptor.RouteValues["controller"]}_{api.ActionDescriptor.RouteValues["action"]}";
        });
    });
    return services;
}
```

Then, on each endpoint mapped in `Vora.Api/Endpoints/*Endpoints.cs`, add `.WithName("GetUserById")` (or similar). Existing `.WithName` calls already serve as operation IDs once the resolver above is in place.

The shape we want: PascalCase verb + noun (`ListUsers`, `GetUserById`, `CreateUser`, `UpdateUser`, `DeleteUser`, `ListLibraryItems`, `StartPlaybackSession`, …). These translate cleanly to Swift method names (`client.listUsers()`) and Kotlin method names (`client.listUsers()`).

### 2. Verify enum serialization is string

`AddVoraJsonOptions` registers `JsonStringEnumConverter`, so enums serialize as strings at the HTTP boundary. Confirm the OpenAPI schema reflects this by checking a sample endpoint that returns an enum — the schema should show `"type": "string", "enum": ["FOO", "BAR"]`, not integer values. If it shows integers, add to `AddSwaggerGen`:

```csharp
options.UseInlineDefinitionsForEnums();
options.MapType<SomeEnum>(() => new OpenApiSchema { Type = "string", Enum = ... });
```

Usually not needed because the JSON converter is global, but verify.

### 3. Document ProblemDetails as the error shape

Standard ASP.NET Core problem-details responses (RFC 7807) are emitted by `UseExceptionHandler()` + `UseStatusCodePages()`. The OpenAPI doc should declare these as error response shapes on every endpoint. Add to `AddSwaggerGen`:

```csharp
options.SupportNonNullableReferenceTypes();
options.IncludeXmlComments(...);  // skip — repo is comment-free
```

Then either decorate endpoints with `.ProducesProblem(StatusCodes.Status400BadRequest)` etc., or add a document filter that adds problem-details responses to every endpoint. The latter is less invasive.

### 4. Verify the schema with a dry-run

After the hardening, fetch the doc and run a no-op codegen to confirm everything generates cleanly:

```bash
curl http://localhost:8080/swagger/v1/swagger.json > /tmp/vora-openapi.json
# Validate it's well-formed:
npx @apidevtools/swagger-cli validate /tmp/vora-openapi.json
```

If `swagger-cli` reports errors (orphan refs, missing schemas, anonymous types without IDs), fix them in `Vora.Api` before scaling out to native client generation.

## Generating the Swift client

Use [`swift-openapi-generator`](https://github.com/apple/swift-openapi-generator), an official Apple tool. Integrate as an Xcode build plugin on the `VoraCore` Swift package.

### Package.swift setup

```swift
// In VoraCore/Package.swift
let package = Package(
    name: "VoraCore",
    platforms: [.iOS(.v17), .tvOS(.v17)],
    products: [
        .library(name: "VoraCore", targets: ["VoraCore"]),
    ],
    dependencies: [
        .package(url: "https://github.com/apple/swift-openapi-generator", from: "1.0.0"),
        .package(url: "https://github.com/apple/swift-openapi-runtime", from: "1.0.0"),
        .package(url: "https://github.com/apple/swift-openapi-urlsession", from: "1.0.0"),
    ],
    targets: [
        .target(
            name: "VoraCore",
            dependencies: [
                .product(name: "OpenAPIRuntime", package: "swift-openapi-runtime"),
                .product(name: "OpenAPIURLSession", package: "swift-openapi-urlsession"),
            ],
            plugins: [
                .plugin(name: "OpenAPIGenerator", package: "swift-openapi-generator"),
            ]
        ),
    ]
)
```

### openapi.yaml + openapi-generator-config.yaml

Drop a copy of `vora-openapi.json` (or a YAML conversion) into `VoraCore/Sources/VoraCore/openapi.yaml` along with a generator config:

```yaml
# VoraCore/Sources/VoraCore/openapi-generator-config.yaml
generate:
  - types
  - client
namingStrategy: idiomatic
accessModifier: public
```

### Build + use

`swift build` from inside `VoraCore` triggers the build plugin, which generates Swift code in `.build/plugins/.../`. Access generated types from anywhere in `VoraCore`:

```swift
import OpenAPIRuntime
import OpenAPIURLSession

public final class VoraClient {
    private let client: Client

    public init(serverURL: URL, tokenProvider: @escaping () -> String?) {
        let transport = URLSessionTransport()
        self.client = Client(
            serverURL: serverURL,
            transport: transport,
            middlewares: [AuthMiddleware(tokenProvider: tokenProvider)]
        )
    }

    public func listLibraryItems(libraryId: String) async throws -> [Components.Schemas.MediaItemVM] {
        let response = try await client.listLibraryItems(.init(path: .init(libraryId: libraryId)))
        return try response.ok.body.json
    }
}
```

### Regeneration workflow

When `Vora.Api` ships a schema change:

1. Run `Vora.Api` locally (`docker-compose up`).
2. `curl http://localhost:8080/swagger/v1/swagger.json > VoraCore/Sources/VoraCore/openapi.yaml` (or `.json` — generator accepts both).
3. `swift build` — generator regenerates client code. Compile errors flag every place a renamed / removed endpoint was used.

Consider committing the OpenAPI doc inside `VoraCore` so the repo is buildable without network access to a running backend. The doc is small (text) and changes via deliberate "sync" commits.

## Generating the Kotlin client

Use the [OpenAPI Generator Gradle plugin](https://openapi-generator.tech/docs/generators/kotlin/) with the `kotlin` generator and `library = jvm-retrofit2` or `library = multiplatform` for Ktor.

Recommendation: `jvm-retrofit2` for Android (battle-tested, Retrofit's coroutine + suspend-fn support is excellent).

### build.gradle.kts setup

```kotlin
// In :core module
plugins {
    id("org.openapi.generator") version "7.10.0"
    kotlin("plugin.serialization")
}

dependencies {
    implementation("com.squareup.retrofit2:retrofit:2.11.0")
    implementation("com.squareup.retrofit2:converter-moshi:2.11.0")
    implementation("com.squareup.okhttp3:okhttp:4.12.0")
    implementation("com.squareup.okhttp3:logging-interceptor:4.12.0")
    implementation("org.jetbrains.kotlinx:kotlinx-serialization-json:1.7.3")
}

openApiGenerate {
    generatorName.set("kotlin")
    inputSpec.set("$rootDir/openapi/vora-openapi.json")
    outputDir.set("$buildDir/generated/openapi")
    apiPackage.set("com.vora.api")
    modelPackage.set("com.vora.api.model")
    invokerPackage.set("com.vora.api.invoker")
    configOptions.set(mapOf(
        "library" to "jvm-retrofit2",
        "useCoroutines" to "true",
        "serializationLibrary" to "moshi",
        "dateLibrary" to "java8",
        "enumPropertyNaming" to "UPPERCASE",
    ))
}

sourceSets {
    main {
        kotlin {
            srcDir("$buildDir/generated/openapi/src/main/kotlin")
        }
    }
}

tasks.compileKotlin {
    dependsOn(tasks.openApiGenerate)
}
```

### Use

```kotlin
import com.vora.api.LibraryApi
import retrofit2.Retrofit
import retrofit2.converter.moshi.MoshiConverterFactory

class VoraClient(serverUrl: String, tokenProvider: () -> String?) {
    private val retrofit = Retrofit.Builder()
        .baseUrl(serverUrl)
        .client(buildOkHttpClient(tokenProvider))
        .addConverterFactory(MoshiConverterFactory.create())
        .build()

    val library: LibraryApi = retrofit.create(LibraryApi::class.java)
    // ... other apis grouped by tag
}

// In a ViewModel:
val items = voraClient.library.listLibraryItems(libraryId = libId)
```

### Regeneration workflow

When `Vora.Api` ships a schema change:

1. Run `Vora.Api` locally.
2. `curl http://localhost:8080/swagger/v1/swagger.json > openapi/vora-openapi.json` (relative to repo root).
3. `./gradlew :core:openApiGenerate :core:compileKotlin` — regenerates and recompiles. Build errors flag every renamed / removed endpoint usage.

## Authentication

The generated clients don't know how Vora's auth headers work. Wrap each with a thin middleware (Swift) or OkHttp interceptor (Kotlin) that:

- Reads the current account / profile token from secure storage (Keychain on Apple, EncryptedSharedPreferences on Android).
- Adds `Authorization: Bearer <token>` to every outbound request.
- Adds the `X-Vora-*` device headers (see `docs/auth-and-devices.md`) — `X-Vora-Device-Id`, `X-Vora-Client`, `X-Vora-Device`, `X-Vora-Device-Type`, `X-Vora-OS`.
- On 401, triggers a re-auth flow (clear token, route user back to login).

The web client does the same thing in `src/api/client.ts`'s axios interceptor; the native shape mirrors it.

## Multi-server support

Vora's web client supports multiple servers per account (per-server token vault). Native clients should mirror this: the `VoraClient` is parameterized by `serverURL` + `tokenProvider`. Switching servers means constructing a new client (or rebinding the URL on an existing one). The generated client classes have a single `serverURL`; wrapping them in `VoraClient` is what makes multi-server tractable.

## Things to watch out for

- **Anonymous response types.** Minimal API endpoints that return `TypedResults.Ok(new { foo, bar })` produce inline anonymous schemas with no name. Generators turn these into ugly `InlineObject1` types. Either return a named record (`*VM` or `*Response`) or add `WithOpenApi(op => op.OperationId = ...)` to give the response a stable name.
- **Polymorphic results.** `Results<Ok<UserVM>, NotFound, ProblemDetails>` is correctly emitted, but `swift-openapi-generator` and the Kotlin generator handle the union case differently. Test before relying on polymorphic returns; prefer single-typed return + status code variation where possible.
- **DateTime handling.** Backend uses `DateTimeOffset`. Generators produce `Date` / `ZonedDateTime` types. Add adapters if you need a custom parser.
- **Nullable reference types.** The `SupportNonNullableReferenceTypes()` option above is required for `string?` to come through as Swift `String?` / Kotlin `String?` instead of always-optional. Verify after the hardening.
- **Pagination.** Vora endpoints that paginate (`?page=`, `?pageSize=`) emit response shapes with explicit `Items` + `TotalCount`. Make sure generated clients model these as paginated collections, not bare arrays.

## Verification

After both clients are wired:

1. Add a deliberate API rename in `Vora.Api` (e.g. rename a `*VM` field). Run codegen on both clients. Both should fail to compile at the call site that used the old name.
2. Add a new endpoint. Run codegen. Both clients should pick it up automatically without any hand-edits.
3. Diff the generated Swift / Kotlin clients across two commits to confirm regeneration is deterministic — the same OpenAPI doc must produce the same output bytes every time.
