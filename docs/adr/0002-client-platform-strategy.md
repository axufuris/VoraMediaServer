# ADR-0002: Native client platforms for Android, Apple, and Roku

**Status:** Accepted (decided 2026-05-26)
**Date:** 2026-05-26
**Deciders:** Andy (project owner)

## Context

`Vora.Api` and `Vora.Web` are feature-complete (see `MEMORY.md` — REVIEW backlog closed 2026-05-26). The next phase is to ship native clients for the platforms where users actually consume media: Android phones, Android TV / Google TV / Nvidia Shield, iOS phones, Apple TV (tvOS), and Roku streaming devices. The web client continues to serve browsers and is the only surface that exposes the admin UI; native clients are playback / library / live-TV / DVR clients without admin.

The constraints that shape this decision:

1. **TV-first quality.** The living-room experience is the most important surface. Apps like Apple TV+, Disney+, Plex, and Jellyfin's tvOS client are all native, because TV requires precise focus animations on weaker hardware, integration with platform players (AVPlayer / ExoPlayer), and idiomatic D-pad navigation that cross-platform frameworks model poorly.
2. **Solo developer.** Andy is the only contributor. Multiplying codebases multiplies long-term maintenance cost, so the architecture must minimise per-platform divergence wherever possible without compromising the TV experience.
3. **Roku is a separate universe.** Roku channels are written in BrightScript with the SceneGraph XML/UI framework. No cross-platform framework targets Roku — never has, never will. A Roku client is a separate codebase regardless of what is chosen for the other platforms. Per the discussion that produced this ADR, Roku is queued for a later phase and is not allowed to constrain decisions for the other platforms.
4. **Existing skill base.** Andy is fluent in C# (the backend) and React/TypeScript (the web client). Swift, Kotlin, and BrightScript are all new. The framework choice should not pretend this away.
5. **Match the web client's design language closely, but adapt for platform.** The cinematic look (`docs/redesign/design-language.md`) — Hero, CinematicBackdrop, MediaPoster, MediaRail, Glass, accent gradients — must travel. The interactions (focus on TV, touch on phone, mouse on web) are platform-specific and must follow native idioms.
6. **No admin UI in v1 of any native client.** Admin tasks happen on the web. A future phone-only admin surface is "maybe" and is not allowed to influence the architecture.

Visual Studio 2026 is the IDE for the backend; an obvious surface-level question is whether `.NET MAUI` should host the mobile clients to keep the Microsoft toolchain unified. The analysis below addresses that.

## Decision

Build native clients per ecosystem, with a shared **contract layer** rather than a shared **renderer layer**:

- **Apple ecosystem** (iPhone + iPad + Apple TV): one Swift codebase using **SwiftUI** for both iOS 17+ and tvOS 17+. Form-factor differences handled via `#if os(tvOS)` blocks and SwiftUI's built-in focus modifiers (`@FocusState`, `.focusable()`). Video via **AVPlayer / AVKit** wrapped in `VideoPlayer`. SignalR via the official Swift client.
- **Android ecosystem** (Android phones + Android TV / Google TV / Nvidia Shield): one Kotlin codebase using **Jetpack Compose** + **Compose for TV**. Form-factor differences handled via `BuildConfig` flavors and Compose's `Modifier.focusable()` / `tv-foundation` primitives. Video via **ExoPlayer** wrapped in Compose. SignalR via Microsoft's Kotlin client.
- **Roku**: separate BrightScript / SceneGraph channel, queued for a later phase. Same backend API surface.
- **Web**: `Vora.Web` stays as-is. Continues to host the admin UI (which no native client will receive in v1). Remains the design reference implementation for the cinematic primitives.

The shared layer between platforms is the **API contract** and the **design tokens** — both emitted as build artifacts from sources of truth that already live in this repo:

- API surface generated from `Vora.Api`'s OpenAPI document via [`swift-openapi-generator`](https://github.com/apple/swift-openapi-generator) on Apple and [`openapi-generator`](https://openapi-generator.tech/) (Kotlin client with coroutines) on Android. Every endpoint, request body, response body, and enum becomes type-safe client code with no hand-maintained DTOs.
- Design tokens emitted from `Vora.Web/src/theme/themes/*.ts` (the `ThemeManifest` type — already the source of truth for web theming) into Swift and Kotlin constants via a small Node script. Every theme rebuild propagates the new accent / surface / spacing / motion values to every client.

Each native repository is its own git repo, independent of `Vora.slnx`. The Vora monorepo holds the backend, the web client, and the emitted contracts (OpenAPI doc + design-token outputs); the native repos consume those contracts.

Android is the **first native target**: Compose for TV is mature, Android emulators are free, and the iteration cycle avoids the Apple Developer Program enrollment friction during the early shape-finding phase.

## Options Considered

### Option A: .NET MAUI

A single C# / XAML codebase targeting Android, iOS, macOS, and Windows from within Visual Studio.

| Dimension | Assessment |
|-----------|------------|
| Complexity | Low to start, high in TV-specific corners |
| Cost | Lowest upfront (one codebase, familiar language) |
| Scalability | Limited — TV story is a community fork at best |
| Team familiarity | Highest (C# everywhere) |

**Pros:** Leverages existing .NET fluency. Single codebase. Stays in VS 2026. The same architectural patterns (DI, async / await, nullable reference types) port over from the backend.
**Cons:** **No first-class tvOS support and no first-class Android TV support.** A community `Maui.tvOS` workstream exists but is not production-grade; Android TV runs only because Android TV is "just Android," which means no leanback / Compose-for-TV focus primitives, no Top Shelf integration, no Channels API integration. For a media app where the living-room experience is the priority, this disqualifies MAUI outright. No path to Apple TV at all.

### Option B: React Native via the `react-native-tvos` fork

A single TypeScript / JSX codebase targeting Android, Android TV, iOS, and tvOS. The fork is the de facto choice for cross-platform TV apps.

| Dimension | Assessment |
|-----------|------------|
| Complexity | Medium |
| Cost | Medium (one mobile codebase + Roku) |
| Scalability | Good per-app; ecosystem lag is a tax |
| Team familiarity | High (existing React/TS fluency from `Vora.Web`) |

**Pros:** Reuses the React/TypeScript skill base from `Vora.Web`. One mobile codebase covers four targets. Some non-renderer code (API services, types, SignalR wiring, domain logic) can be lifted directly from the web. Strong precedent — Plex, Jellyfin, several streaming services ship RN TV clients.
**Cons:** TV-quality bar is *good*, not *best*. Focus management on TV requires hand-rolled abstractions on top of `react-native-tvos` primitives (`TVFocusGuideView`, `nextFocusUp/Down/Left/Right`). The fork lags upstream React Native by 1–2 minor versions; community native modules occasionally break on tvOS because nobody tested them there. Video player (`react-native-video`) is mature but loses out on platform-native niceties (AVKit's built-in scrub thumbnails, ExoPlayer's `MediaSession` integration). Over a five-year maintenance window, this friction compounds.

### Option C: Flutter

A single Dart codebase targeting Android, iOS, web, desktop.

| Dimension | Assessment |
|-----------|------------|
| Complexity | Medium |
| Cost | Medium |
| Scalability | Adequate for phone, poor for TV |
| Team familiarity | Lowest (Dart is new) |

**Pros:** Strong rendering performance. Single codebase. Excellent design fidelity (custom rendering engine respects designs to the pixel).
**Cons:** **No real tvOS or Android TV support.** Disqualified by the same constraint that disqualifies MAUI. Adopting Dart adds a third primary language without solving the TV problem.

### Option D: Native per platform (chosen)

Swift / SwiftUI for the Apple ecosystem, Kotlin / Compose (+ Compose for TV) for the Android ecosystem, BrightScript / SceneGraph for Roku (later), shared via emitted contracts.

| Dimension | Assessment |
|-----------|------------|
| Complexity | Medium-High |
| Cost | Highest in lines-of-code, lowest in framework friction |
| Scalability | Best — each client gets first-class platform tooling |
| Team familiarity | Lowest (two new languages, two new IDEs, plus Xcode toolchain) |

**Pros:** Apple TV and Android TV get their respective platforms' best focus engines, video integration, and platform-conformance affordances (Top Shelf, Channels integration, Picture-in-Picture, AirPlay 2, Chromecast). Modern Apple platforms encourage one SwiftUI codebase across iPhone / iPad / Apple TV with conditional layouts, so "two ecosystems" collapses to **two mobile codebases**, not four. Same for Compose on the Android side. No fork lag, no framework abstraction tax, no waiting on community libraries to add tvOS support. Long-term stability is the strongest of any option: Apple and Google ship updates that improve these stacks, not break them. Design language travels via emitted tokens; API surface travels via OpenAPI codegen — no source-level sharing required.
**Cons:** Two new languages and two new IDEs (Xcode for Apple, Android Studio for Android). VS 2026 still owns the backend but no longer owns the clients. Mac required for any Apple work (already true for RN; not new). Source code does not literally share with `Vora.Web` — only contracts do. Initial learning curve is real, especially for SwiftUI focus management on tvOS.

## Trade-off Analysis

The decisive question is **Option B (React Native) vs Option D (native)**. Options A and C are rejected on the TV constraint alone.

The case for Option B is fewer codebases and skill-base continuity from `Vora.Web`. Both real benefits. The case against is that the friction is *recurring* and *invisible until it bites*: an upstream RN release that the tvOS fork hasn't picked up yet, a `react-native-video` regression on Apple TV, a focus-trap edge case in `TVFocusGuideView` that only shows up with three-deep nested rails. None of these are show-stoppers; all of them are paper cuts that accumulate over years of maintenance for a solo developer.

The case for Option D is that every paper cut Option B accumulates is one Option D doesn't have. SwiftUI on tvOS has best-in-class focus handling (`@FocusState`, `.focusable()`, focus sections), video integration is one line (`VideoPlayer(player: avPlayer)`), and platform updates make things better, not worse. Compose for TV on the Android side has reached a similar level of maturity. The cost is up-front: two unfamiliar languages, two new IDEs. The benefit is long-term: less framework to fight.

The user's two priorities were stated explicitly: **best TV experience** and **frictionless long-term maintenance**. These pull in opposite directions only if "frictionless" means "fewest codebases." Reframed as "least framework friction over years of solo maintenance," Option D wins on both axes. Option B trades up-front simplicity for ongoing tax.

The source-sharing argument for Option B is weaker than it appears. The portable parts of `Vora.Web` are the **API client layer**, the **type definitions**, and the **design tokens**. All three can be made portable via emitted artifacts (OpenAPI codegen, Style-Dictionary-style token emission) without sharing React components — and the React components themselves don't fit a TV input model anyway, so they were going to be rewritten regardless. With the contract layer extracted, the only thing Option B actually shares is JSX renderer code that has to be rewritten for the TV form factor inside Option B as well. The "single codebase" turns out to be one codebase with three rendering modes (web / phone / TV), not one renderer for everywhere.

On the Roku question: Roku must be a separate codebase under every option. It does not influence A-vs-B-vs-D. It is staged after the Apple and Android clients ship.

## Consequences

What becomes easier:

- The TV experience can be polished to match the platform-native bar (Apple TV+ / Plex / Jellyfin level) without fighting a cross-platform abstraction.
- Each client uses its platform's best primitives without compromise: SwiftUI focus + AVKit on Apple, Compose for TV + ExoPlayer on Android.
- Updates to iOS, Android, tvOS, Compose, SwiftUI land directly — no fork to wait for.
- Adding a Roku channel later doesn't require unwinding any cross-platform abstraction; the architectural shape is already "thin native clients on top of a shared backend contract."
- The web client is no longer obligated to be a portable design source — it gets to evolve at its own pace as the browser / admin client it actually is. Native clients reference the design tokens, not the React tree.

What becomes harder:

- Two new languages (Swift, Kotlin) and two new IDEs (Xcode, Android Studio) to learn and maintain workflows for. Mac required for Apple work.
- The same feature shipped to all clients means writing it three times (web, Apple, Android) plus eventually Roku. Discipline becomes: ship to one client, validate the design, then port. The shared contract layer prevents drift in data shapes; the design tokens prevent drift in look.
- The `Vora.Api` OpenAPI document becomes load-bearing for all three native clients. Schema regressions ripple to every regenerated client SDK. Operation IDs, problem-details shape, and enum-as-string contracts now need to be defended at the API layer (see `docs/clients/openapi-codegen.md`).
- The `ThemeManifest` TS type becomes a cross-language schema. Adding a new token slot means updating the emitter for Swift and Kotlin as well as the CSS variable list. (Cost: a few lines per token.)
- Authentication and token storage now have to be re-implemented per platform — Keychain on Apple, EncryptedSharedPreferences on Android. The HTTP and SignalR wiring is similar but not source-shared.

What we'll need to revisit:

- If the per-client maintenance load proves unsustainable for a solo developer, the fallback is **(a)** retreat to React Native and accept the TV-quality compromise, or **(b)** pause a platform (typically Android TV, since Apple TV has a stronger user-money story). The decision is reversible at any time per-platform; nothing about the shared-contract layer requires a particular renderer.
- The choice of Kotlin Multiplatform (KMP) for sharing business logic across Apple and Android was explicitly not adopted in v1 — the OpenAPI client layer is the thinnest, lowest-risk way to share what actually matters. KMP can be layered in later if a substantial body of client-side business logic (e.g. smart-playlist evaluation, marker post-processing) is duplicated in both clients.
- If Apple's SwiftUI tvOS focus story regresses, or if Compose for TV stalls, the per-ecosystem decisions can be revisited independently of each other.
- Admin on mobile (phones only) is deferred. If admin is later added, it ships into the existing native phone code as an additional surface gated by the existing `is_server_admin` flag — it does not become a separate app.

## Action Items

Foundations (this ADR's immediate follow-ups):

1. [ ] Promote the design-token schema to a documented source of truth (`docs/clients/design-tokens.md`) covering colors, typography, radii, shadows, motion, layout. Reference the existing `ThemeManifest` TS type as the schema.
2. [ ] Add a token emitter under `Vora.Web/scripts/emit-tokens.ts` (or similar) that reads a `ThemeManifest` and outputs `VoraTokens.swift` + `VoraTokens.kt` files. Emit to `dist/tokens/` for native repos to consume.
3. [ ] Document the OpenAPI codegen pipeline (`docs/clients/openapi-codegen.md`) — commands for `swift-openapi-generator` and the Kotlin `openapi-generator` against `/swagger/v1/swagger.json`.
4. [ ] Harden the OpenAPI doc for codegen: configure Swashbuckle with explicit `operationId`s on every endpoint, confirm enum-as-string flows through to the schema, confirm `ProblemDetails` shape is documented. Verify with a dry-run `swift-openapi-generator` invocation.
5. [ ] Document the client primitive specs (`docs/clients/primitive-specs.md`) — the contract that `Hero`, `CinematicBackdrop`, `MediaPoster`, `MediaStill`, `MediaRail`, `Glass`, `QualityPanel`, `LetterRail`, `EmptyState`, `NowPlayingBar` must satisfy on every renderer. Props, behavior, focus rules, motion. The reference implementation stays in `Vora.Web`.
6. [ ] Add an ESLint `no-restricted-imports` guard in `Vora.Web` blocking `src/api/**` and (when it exists) `src/types/**` from importing `src/components/**` or `src/pages/**`. Preserves the option to extract `@vora/api-types` later.
7. [ ] Add the new docs to `CLAUDE.md`'s docs index and link this ADR from there.

Android client (first target):

8. [ ] Create the `Vora.Android` repository. Single Gradle project with `app-mobile` and `app-tv` Android variants sharing a `core` module that holds the generated OpenAPI client, the emitted token file, repositories, and view models.
9. [ ] Wire OpenAPI codegen into Gradle: a `:core:generateApiClient` task that consumes the OpenAPI doc and emits Kotlin client code under `core/build/generated/`.
10. [ ] Wire the design-token consumer: copy or symlink `dist/tokens/VoraTokens.kt` into `core/src/main/kotlin/` as part of the build; expose tokens via a `VoraTheme` CompositionLocal.
11. [ ] Build the focus + theming foundation: `Modifier.focusable()` wrappers, the focus-ring glow, the cinematic backdrop primitive, the poster rail. Validate on Android TV emulator (firetv / nexus-player image) before any feature work.
12. [ ] Implement auth + profile selection end-to-end as the first vertical slice.
13. [ ] Implement Home → Library → Item Detail → Playback (ExoPlayer) as the second vertical slice. Markers, video thumbnails, audio quality, subtitles layered in after.
14. [ ] Live TV + DVR as a separate vertical slice — the EPG grid is the hardest focus problem in the app.

Apple client (second target):

15. [ ] Create the `Vora.Apple` repository. Single Xcode workspace with `Vora-iOS` and `Vora-tvOS` targets sharing a `VoraCore` Swift package that holds the generated OpenAPI client, the emitted token file, repositories, and view models.
16. [ ] Wire `swift-openapi-generator` as a build plugin on `VoraCore`. Wire token-file consumption similarly.
17. [ ] Repeat the Android slices in priority order: focus / theming foundation → auth + profile → Home / Library / Detail / Playback → Live TV + DVR.

Roku (later phase):

18. [ ] Create the `Vora.Roku` repository when prior clients are stable. SceneGraph + BrightScript. Same backend, same contracts (Roku has no formal OpenAPI generator — hand-written client against the OpenAPI doc).

Verification:

19. [ ] Build the Android TV emulator slice (`vora-default` cinema template, one focusable poster rail rendering against a real `Vora.Api` instance) as the "is this approach actually working" gate before scaling out to phones.
20. [ ] Once two clients have shipped a vertical slice, confirm the design-token emitter has not drifted: change one token in the manifest, regenerate, observe the change land on web + Android (+ Apple) without source edits.
21. [ ] Confirm OpenAPI regen surfaces backend schema changes as compile errors on every client (rename an endpoint, regenerate, confirm Android + Apple builds break in the expected place).
