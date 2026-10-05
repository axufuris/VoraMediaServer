# Generated Swift and Kotlin clients

Concrete commands for generating the Swift and Kotlin clients from `Vora.Api`'s OpenAPI document, and the auth and multi-server wrapping the generated code needs. How the document is produced and hardened for codegen is in [`docs/clients/openapi-codegen.md`](openapi-codegen.md).

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
